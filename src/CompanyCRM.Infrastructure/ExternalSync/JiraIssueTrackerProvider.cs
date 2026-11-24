using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CompanyCRM.Application.DTOs;
using CompanyCRM.Application.Interfaces;
using CompanyCRM.Domain.Entities;
using CompanyCRM.Domain.Enums;
using CompanyCRM.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace CompanyCRM.Infrastructure.ExternalSync;

public class JiraIssueTrackerProvider : IIssueTrackerProvider
{
    private readonly HttpClient _httpClient;
    private readonly JiraSimulator _simulator;
    private readonly ILogger<JiraIssueTrackerProvider> _logger;

    public ExternalSourceType SourceType => ExternalSourceType.Jira;

    public JiraIssueTrackerProvider(
        HttpClient httpClient,
        JiraSimulator simulator,
        ILogger<JiraIssueTrackerProvider> logger)
    {
        _httpClient = httpClient;
        _simulator = simulator;
        _logger = logger;
    }

    public async Task<ExternalWorkItemDto> PushWorkItemAsync(
        ProjectExternalConfig config,
        WorkItem workItem,
        CancellationToken ct = default)
    {
        // 1. Sandbox Fallback when simulated or without credentials
        if (config.IsSimulatedMode || string.IsNullOrWhiteSpace(config.Pat))
        {
            _logger.LogInformation("Routing PushWorkItem to JiraSimulator for {WorkItemCode}", workItem.Code);
            return await _simulator.PushWorkItemAsync(config, workItem, ct);
        }

        // 2. Prepare Live Jira REST API call
        var baseUrl = config.OrgUrl?.TrimEnd('/') ?? throw new ArgumentException("OrgUrl (Jira Base URL) is required for live sync");
        var projectKey = config.ProjectName?.Trim().ToUpperInvariant() ?? "CRM";

        var authHeader = CreateAuthHeader(config.Pat);
        var jiraIssueType = MapTypeToJira(workItem.Type);
        var jiraStatus = MapStatusToJira(workItem.Status);
        var jiraPriority = MapPriorityToJira(workItem.Priority);

        var request = CreatePushHttpRequest(baseUrl, projectKey, workItem, jiraIssueType, jiraPriority);
        request.Headers.Authorization = authHeader;
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var response = await _httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("Jira API error ({StatusCode}): {Body}", response.StatusCode, errorBody);
            throw new HttpRequestException($"Jira API error: {response.StatusCode} - {errorBody}");
        }

        var responseJson = await response.Content.ReadAsStringAsync(ct);
        return ParseJiraResponseJson(responseJson, baseUrl, workItem, jiraStatus, jiraIssueType);
    }

    private static HttpRequestMessage CreatePushHttpRequest(
        string baseUrl,
        string projectKey,
        WorkItem workItem,
        string jiraIssueType,
        string jiraPriority)
    {
        if (string.IsNullOrWhiteSpace(workItem.ExternalId))
        {
            var requestUri = $"{baseUrl}/rest/api/2/issue";
            var request = new HttpRequestMessage(HttpMethod.Post, requestUri);

            var createPayload = new
            {
                fields = new Dictionary<string, object?>
                {
                    ["project"] = new { key = projectKey },
                    ["summary"] = workItem.Title,
                    ["description"] = workItem.Description ?? string.Empty,
                    ["issuetype"] = new { name = jiraIssueType },
                    ["priority"] = new { name = jiraPriority }
                }
            };

            if (!string.IsNullOrWhiteSpace(workItem.Assignee?.Email))
            {
                createPayload.fields["assignee"] = new { name = workItem.Assignee.Email };
            }

            request.Content = new StringContent(JsonSerializer.Serialize(createPayload), Encoding.UTF8, "application/json");
            return request;
        }
        else
        {
            var requestUri = $"{baseUrl}/rest/api/2/issue/{Uri.EscapeDataString(workItem.ExternalId)}";
            var request = new HttpRequestMessage(HttpMethod.Put, requestUri);

            var updatePayload = new
            {
                fields = new Dictionary<string, object?>
                {
                    ["summary"] = workItem.Title,
                    ["description"] = workItem.Description ?? string.Empty,
                    ["priority"] = new { name = jiraPriority }
                }
            };

            if (!string.IsNullOrWhiteSpace(workItem.Assignee?.Email))
            {
                updatePayload.fields["assignee"] = new { name = workItem.Assignee.Email };
            }

            request.Content = new StringContent(JsonSerializer.Serialize(updatePayload), Encoding.UTF8, "application/json");
            return request;
        }
    }

    public async Task<IReadOnlyList<ExternalWorkItemDto>> FetchRemoteWorkItemsAsync(
        ProjectExternalConfig config,
        DateTimeOffset? modifiedSince = null,
        CancellationToken ct = default)
    {
        if (config.IsSimulatedMode || string.IsNullOrWhiteSpace(config.Pat))
        {
            return await _simulator.FetchRemoteWorkItemsAsync(config, modifiedSince, ct);
        }

        var baseUrl = config.OrgUrl?.TrimEnd('/') ?? throw new ArgumentException("OrgUrl is required for live sync");
        var projectKey = config.ProjectName?.Trim().ToUpperInvariant() ?? "CRM";

        var jql = $"project = \"{projectKey}\"";
        if (modifiedSince.HasValue)
        {
            var formattedDate = modifiedSince.Value.ToString("yyyy-MM-dd HH:mm");
            jql += $" AND updated >= \"{formattedDate}\"";
        }
        jql += " ORDER BY updated ASC";

        var searchUri = $"{baseUrl}/rest/api/2/search?jql={Uri.EscapeDataString(jql)}&maxResults=100";
        var request = new HttpRequestMessage(HttpMethod.Get, searchUri);
        request.Headers.Authorization = CreateAuthHeader(config.Pat);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var response = await _httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("Jira JQL search error ({StatusCode}): {Body}", response.StatusCode, errorBody);
            throw new HttpRequestException($"Jira search failed: {response.StatusCode} - {errorBody}");
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        return ParseJiraSearchJson(json, baseUrl);
    }

    public Task<ExternalWorkItemDto?> ParseWebhookPayloadAsync(
        string rawJson,
        IDictionary<string, string> headers,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return Task.FromResult<ExternalWorkItemDto?>(null);
        }

        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;

            if (!root.TryGetProperty("issue", out var issueElement) || issueElement.ValueKind != JsonValueKind.Object)
            {
                return Task.FromResult<ExternalWorkItemDto?>(null);
            }

            var externalId = issueElement.TryGetProperty("key", out var keyProp)
                ? keyProp.GetString() ?? string.Empty
                : string.Empty;

            if (string.IsNullOrWhiteSpace(externalId))
            {
                return Task.FromResult<ExternalWorkItemDto?>(null);
            }

            var selfUrl = issueElement.TryGetProperty("self", out var selfProp) ? selfProp.GetString() : null;
            var baseUrl = !string.IsNullOrWhiteSpace(selfUrl) && Uri.TryCreate(selfUrl, UriKind.Absolute, out var uri)
                ? $"{uri.Scheme}://{uri.Authority}"
                : "https://jira.local";

            var externalUrl = $"{baseUrl}/browse/{externalId}";
            var fieldsData = ParseWebhookFields(issueElement);

            return Task.FromResult<ExternalWorkItemDto?>(new ExternalWorkItemDto(
                ExternalId: externalId,
                ExternalUrl: externalUrl,
                Title: fieldsData.Title,
                Description: fieldsData.Description,
                Status: fieldsData.Status,
                Type: fieldsData.Type,
                AssigneeEmail: fieldsData.AssigneeEmail,
                LastModifiedAt: fieldsData.UpdatedDate ?? DateTimeOffset.UtcNow));
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Failed to parse Jira webhook JSON payload");
            return Task.FromResult<ExternalWorkItemDto?>(null);
        }
    }

    private static (string Title, string? Description, string Status, string Type, string? AssigneeEmail, DateTimeOffset? UpdatedDate) ParseWebhookFields(JsonElement issueElement)
    {
        string title = string.Empty;
        string? description = null;
        string status = "To Do";
        string type = "Task";
        string? assigneeEmail = null;
        DateTimeOffset? updatedDate = null;

        if (issueElement.TryGetProperty("fields", out var fields) && fields.ValueKind == JsonValueKind.Object)
        {
            title = GetSafeString(fields, "summary") ?? string.Empty;
            description = GetSafeString(fields, "description");

            if (fields.TryGetProperty("status", out var statusProp) && statusProp.ValueKind == JsonValueKind.Object)
            {
                status = GetSafeString(statusProp, "name") ?? "To Do";
            }

            if (fields.TryGetProperty("issuetype", out var typeProp) && typeProp.ValueKind == JsonValueKind.Object)
            {
                type = GetSafeString(typeProp, "name") ?? "Task";
            }

            if (fields.TryGetProperty("updated", out var updatedProp) &&
                updatedProp.ValueKind == JsonValueKind.String &&
                updatedProp.TryGetDateTimeOffset(out var uDate))
            {
                updatedDate = uDate;
            }

            if (fields.TryGetProperty("assignee", out var assigneeProp) && assigneeProp.ValueKind == JsonValueKind.Object)
            {
                assigneeEmail = GetSafeString(assigneeProp, "emailAddress") ?? GetSafeString(assigneeProp, "name");
            }
        }

        return (title, description, status, type, assigneeEmail, updatedDate);
    }

    private static AuthenticationHeaderValue CreateAuthHeader(string pat)
    {
        if (pat.Contains(':'))
        {
            // Basic Auth with username:api_token
            return new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes(pat)));
        }
        // Bearer PAT for Jira Data Center
        return new AuthenticationHeaderValue("Bearer", pat);
    }

    private static string? GetSafeString(JsonElement obj, string propertyName)
    {
        if (obj.ValueKind != JsonValueKind.Object) return null;
        if (!obj.TryGetProperty(propertyName, out var prop)) return null;
        return prop.ValueKind == JsonValueKind.String ? prop.GetString() : null;
    }

    public static string MapTypeToJira(WorkItemType type) => type switch
    {
        WorkItemType.Bug => "Bug",
        WorkItemType.Story => "Story",
        _ => "Task"
    };

    public static WorkItemType MapTypeFromJira(string jiraType) => jiraType.ToLowerInvariant() switch
    {
        "bug" => WorkItemType.Bug,
        "story" => WorkItemType.Story,
        _ => WorkItemType.Task
    };

    public static string MapStatusToJira(WorkItemStatus status) => status switch
    {
        WorkItemStatus.Draft => "To Do",
        WorkItemStatus.PendingReview => "To Do",
        WorkItemStatus.ClarificationRequested => "To Do",
        WorkItemStatus.Denied => "Done",
        WorkItemStatus.Backlog => "To Do",
        WorkItemStatus.Todo => "To Do",
        WorkItemStatus.InProgress => "In Progress",
        WorkItemStatus.InVerify => "In Review",
        WorkItemStatus.Done => "Done",
        _ => "To Do"
    };

    public static WorkItemStatus MapStatusFromJira(string jiraStatus) => jiraStatus.Trim().ToLowerInvariant() switch
    {
        "to do" or "open" or "backlog" or "new" => WorkItemStatus.Todo,
        "in progress" or "started" or "active" => WorkItemStatus.InProgress,
        "in review" or "code review" or "under review" or "in verify" => WorkItemStatus.InVerify,
        "done" or "resolved" or "closed" => WorkItemStatus.Done,
        _ => WorkItemStatus.Todo
    };

    public static string MapPriorityToJira(WorkItemPriority priority) => priority switch
    {
        WorkItemPriority.Low => "Low",
        WorkItemPriority.Medium => "Medium",
        WorkItemPriority.High => "High",
        WorkItemPriority.Critical => "Highest",
        _ => "Medium"
    };

    private static ExternalWorkItemDto ParseJiraResponseJson(
        string json,
        string baseUrl,
        WorkItem workItem,
        string mappedStatus,
        string mappedType)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var key = root.TryGetProperty("key", out var keyProp)
            ? keyProp.GetString() ?? workItem.ExternalId ?? "JIRA-UNKNOWN"
            : workItem.ExternalId ?? "JIRA-UNKNOWN";

        return new ExternalWorkItemDto(
            ExternalId: key,
            ExternalUrl: $"{baseUrl}/browse/{key}",
            Title: workItem.Title,
            Description: workItem.Description,
            Status: mappedStatus,
            Type: mappedType,
            AssigneeEmail: workItem.Assignee?.Email,
            LastModifiedAt: DateTimeOffset.UtcNow);
    }

    private static IReadOnlyList<ExternalWorkItemDto> ParseJiraSearchJson(string json, string baseUrl)
    {
        using var doc = JsonDocument.Parse(json);
        var list = new List<ExternalWorkItemDto>();

        if (!doc.RootElement.TryGetProperty("issues", out var issues) || issues.ValueKind != JsonValueKind.Array)
        {
            return list;
        }

        foreach (var issue in issues.EnumerateArray())
        {
            var item = ParseJiraSearchItem(issue, baseUrl);
            if (item != null)
            {
                list.Add(item);
            }
        }

        return list;
    }

    private static ExternalWorkItemDto? ParseJiraSearchItem(JsonElement issue, string baseUrl)
    {
        var key = issue.GetProperty("key").GetString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(key)) return null;

        var (title, description, status, type, assigneeEmail, updated) = ParseSearchItemFields(issue);

        return new ExternalWorkItemDto(
            ExternalId: key,
            ExternalUrl: $"{baseUrl}/browse/{key}",
            Title: title,
            Description: description,
            Status: status,
            Type: type,
            AssigneeEmail: assigneeEmail,
            LastModifiedAt: updated ?? DateTimeOffset.UtcNow);
    }

    private static (string Title, string? Description, string Status, string Type, string? AssigneeEmail, DateTimeOffset? Updated) ParseSearchItemFields(JsonElement issue)
    {
        string title = string.Empty;
        string? description = null;
        string status = "To Do";
        string type = "Task";
        string? assigneeEmail = null;
        DateTimeOffset? updated = null;

        if (issue.TryGetProperty("fields", out var f) && f.ValueKind == JsonValueKind.Object)
        {
            title = GetSafeString(f, "summary") ?? string.Empty;
            description = GetSafeString(f, "description");

            if (f.TryGetProperty("status", out var s) && s.ValueKind == JsonValueKind.Object)
            {
                status = GetSafeString(s, "name") ?? "To Do";
            }

            if (f.TryGetProperty("issuetype", out var it) && it.ValueKind == JsonValueKind.Object)
            {
                type = GetSafeString(it, "name") ?? "Task";
            }

            if (f.TryGetProperty("updated", out var u) && u.ValueKind == JsonValueKind.String && u.TryGetDateTimeOffset(out var uVal))
            {
                updated = uVal;
            }

            if (f.TryGetProperty("assignee", out var a) && a.ValueKind == JsonValueKind.Object)
            {
                assigneeEmail = GetSafeString(a, "emailAddress") ?? GetSafeString(a, "name");
            }
        }

        return (title, description, status, type, assigneeEmail, updated);
    }
}
