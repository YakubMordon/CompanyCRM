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

public class GitHubIssueTrackerProvider : IIssueTrackerProvider
{
    private readonly HttpClient _httpClient;
    private readonly GitHubSimulator _simulator;
    private readonly ILogger<GitHubIssueTrackerProvider> _logger;

    public ExternalSourceType SourceType => ExternalSourceType.GitHub;

    public GitHubIssueTrackerProvider(
        HttpClient httpClient,
        GitHubSimulator simulator,
        ILogger<GitHubIssueTrackerProvider> logger)
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
        if (config.IsSimulatedMode || string.IsNullOrWhiteSpace(config.Pat))
        {
            _logger.LogInformation("Routing PushWorkItem to GitHubSimulator for {WorkItemCode}", workItem.Code);
            return await _simulator.PushWorkItemAsync(config, workItem, ct);
        }

        var apiBaseUrl = ResolveApiBaseUrl(config.OrgUrl);
        var repo = ResolveOwnerRepo(config.ProjectName);
        var state = MapStatusToGitHub(workItem.Status);
        var labels = MapTypeToGitHubLabels(workItem.Type);

        var request = CreatePushHttpRequest(apiBaseUrl, repo, workItem, state, labels);
        AddGitHubHeaders(request, config.Pat);

        var response = await _httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("GitHub API error ({StatusCode}): {Body}", response.StatusCode, errorBody);
            throw new HttpRequestException($"GitHub API error: {response.StatusCode} - {errorBody}");
        }

        var responseJson = await response.Content.ReadAsStringAsync(ct);
        return ParseGitHubIssueResponseJson(responseJson, workItem);
    }

    public async Task<IReadOnlyList<ExternalWorkItemDto>> FetchRemoteWorkItemsAsync(
        ProjectExternalConfig config,
        DateTimeOffset? modifiedSince = null,
        CancellationToken ct = default)
    {
        if (config.IsSimulatedMode || string.IsNullOrWhiteSpace(config.Pat))
        {
            _logger.LogInformation("Routing FetchRemoteWorkItems to GitHubSimulator for project {ProjectId}", config.ProjectId);
            return await _simulator.FetchRemoteWorkItemsAsync(config, modifiedSince, ct);
        }

        var apiBaseUrl = ResolveApiBaseUrl(config.OrgUrl);
        var repo = ResolveOwnerRepo(config.ProjectName);

        var requestUri = $"{apiBaseUrl}/repos/{repo}/issues?state=all&per_page=100";
        if (modifiedSince.HasValue)
        {
            requestUri += $"&since={Uri.EscapeDataString(modifiedSince.Value.UtcDateTime.ToString("O"))}";
        }

        var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        AddGitHubHeaders(request, config.Pat);

        var response = await _httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("GitHub fetch error ({StatusCode}): {Body}", response.StatusCode, errorBody);
            throw new HttpRequestException($"GitHub API error: {response.StatusCode} - {errorBody}");
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        return ParseGitHubIssuesListJson(json);
    }

    public Task<ExternalWorkItemDto?> ParseWebhookPayloadAsync(
        string rawJson,
        IDictionary<string, string> headers,
        CancellationToken ct = default)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;

            var eventType = headers.TryGetValue("X-GitHub-Event", out var evt)
                ? evt
                : null;

            if (root.TryGetProperty("pull_request", out var prElement) && prElement.ValueKind == JsonValueKind.Object)
            {
                return Task.FromResult<ExternalWorkItemDto?>(ParseIssueOrPrElement(prElement, isPullRequest: true));
            }

            if (root.TryGetProperty("issue", out var issueElement) && issueElement.ValueKind == JsonValueKind.Object)
            {
                var isPr = issueElement.TryGetProperty("pull_request", out _);
                return Task.FromResult<ExternalWorkItemDto?>(ParseIssueOrPrElement(issueElement, isPullRequest: isPr));
            }

            if (string.Equals(eventType, "ping", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult<ExternalWorkItemDto?>(null);
            }

            return Task.FromResult<ExternalWorkItemDto?>(null);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse GitHub webhook payload");
            return Task.FromResult<ExternalWorkItemDto?>(null);
        }
    }

    private static HttpRequestMessage CreatePushHttpRequest(
        string apiBaseUrl,
        string repo,
        WorkItem workItem,
        string state,
        string[] labels)
    {
        if (string.IsNullOrWhiteSpace(workItem.ExternalId))
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"{apiBaseUrl}/repos/{repo}/issues");
            var payload = new
            {
                title = workItem.Title,
                body = workItem.Description ?? string.Empty,
                labels
            };
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            return request;
        }
        else
        {
            var issueNumber = ExtractIssueNumber(workItem.ExternalId);
            var request = new HttpRequestMessage(HttpMethod.Patch, $"{apiBaseUrl}/repos/{repo}/issues/{issueNumber}");
            var payload = new
            {
                title = workItem.Title,
                body = workItem.Description ?? string.Empty,
                state,
                labels
            };
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            return request;
        }
    }

    private static void AddGitHubHeaders(HttpRequestMessage request, string pat)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", pat);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        request.Headers.UserAgent.ParseAdd("CompanyCRM-Sync/1.0");
    }

    private static string ResolveApiBaseUrl(string? orgUrl)
    {
        if (string.IsNullOrWhiteSpace(orgUrl)) return "https://api.github.com";
        var trimmed = orgUrl.TrimEnd('/');
        if (trimmed.Equals("https://github.com", StringComparison.OrdinalIgnoreCase))
        {
            return "https://api.github.com";
        }
        return trimmed;
    }

    private static string ResolveOwnerRepo(string? projectName)
    {
        if (string.IsNullOrWhiteSpace(projectName))
        {
            throw new ArgumentException("ProjectName (owner/repo) is required for live GitHub sync");
        }
        var trimmed = projectName.Trim().Trim('/');
        if (!trimmed.Contains('/'))
        {
            throw new ArgumentException("GitHub ProjectName must be in 'owner/repo' format");
        }
        return trimmed;
    }

    private static string ExtractIssueNumber(string externalId) =>
        externalId.Replace("GH-PR-", "", StringComparison.OrdinalIgnoreCase)
                  .Replace("GH-", "", StringComparison.OrdinalIgnoreCase)
                  .TrimStart('#');

    private static ExternalWorkItemDto ParseGitHubIssueResponseJson(string json, WorkItem fallback)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var parsed = ParseIssueOrPrElement(root, isPullRequest: root.TryGetProperty("pull_request", out _));
        if (parsed != null) return parsed;

        return new ExternalWorkItemDto(
            ExternalId: fallback.ExternalId ?? "GH-UNKNOWN",
            ExternalUrl: fallback.ExternalUrl ?? string.Empty,
            Title: fallback.Title,
            Description: fallback.Description,
            Status: MapStatusToGitHub(fallback.Status),
            Type: fallback.Type.ToString(),
            AssigneeEmail: fallback.Assignee?.Email,
            LastModifiedAt: DateTimeOffset.UtcNow);
    }

    private static IReadOnlyList<ExternalWorkItemDto> ParseGitHubIssuesListJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var list = new List<ExternalWorkItemDto>();
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return list;

        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var isPr = item.TryGetProperty("pull_request", out _);
            var parsed = ParseIssueOrPrElement(item, isPullRequest: isPr);
            if (parsed != null) list.Add(parsed);
        }

        return list;
    }

    private static ExternalWorkItemDto? ParseIssueOrPrElement(JsonElement el, bool isPullRequest)
    {
        if (!el.TryGetProperty("number", out var numProp) || !numProp.TryGetInt32(out var number))
        {
            return null;
        }

        var externalId = isPullRequest ? $"GH-PR-{number}" : $"GH-{number}";
        var htmlUrl = GetSafeString(el, "html_url") ?? string.Empty;
        var title = GetSafeString(el, "title") ?? string.Empty;
        var body = GetSafeString(el, "body");
        var state = GetSafeString(el, "state") ?? "open";

        if (isPullRequest && el.TryGetProperty("merged", out var mergedProp) && mergedProp.ValueKind == JsonValueKind.True)
        {
            state = "merged";
        }
        else if (isPullRequest && el.TryGetProperty("draft", out var draftProp) && draftProp.ValueKind == JsonValueKind.True && state == "open")
        {
            state = "draft";
        }

        var labels = new List<string>();
        if (el.TryGetProperty("labels", out var labelsProp) && labelsProp.ValueKind == JsonValueKind.Array)
        {
            foreach (var label in labelsProp.EnumerateArray())
            {
                var name = GetSafeString(label, "name");
                if (!string.IsNullOrWhiteSpace(name)) labels.Add(name);
            }
        }

        string? assigneeEmail = null;
        if (el.TryGetProperty("assignee", out var assigneeProp) && assigneeProp.ValueKind == JsonValueKind.Object)
        {
            assigneeEmail = GetSafeString(assigneeProp, "email") ?? GetSafeString(assigneeProp, "login");
        }

        DateTimeOffset updatedAt = DateTimeOffset.UtcNow;
        if (el.TryGetProperty("updated_at", out var updatedProp) &&
            updatedProp.ValueKind == JsonValueKind.String &&
            updatedProp.TryGetDateTimeOffset(out var parsedUpdated))
        {
            updatedAt = parsedUpdated;
        }

        return new ExternalWorkItemDto(
            ExternalId: externalId,
            ExternalUrl: htmlUrl,
            Title: title,
            Description: body,
            Status: state,
            Type: MapTypeFromGitHubLabels(labels, isPullRequest).ToString(),
            AssigneeEmail: assigneeEmail,
            LastModifiedAt: updatedAt);
    }

    private static string? GetSafeString(JsonElement obj, string propertyName)
    {
        if (obj.ValueKind != JsonValueKind.Object) return null;
        if (!obj.TryGetProperty(propertyName, out var prop)) return null;
        return prop.ValueKind == JsonValueKind.String ? prop.GetString() : null;
    }

    public static string MapStatusToGitHub(WorkItemStatus status) => status switch
    {
        WorkItemStatus.Done or WorkItemStatus.Denied => "closed",
        _ => "open"
    };

    public static WorkItemStatus MapStatusFromGitHub(string? ghStatus) => ghStatus?.Trim().ToLowerInvariant() switch
    {
        "closed" or "merged" => WorkItemStatus.Done,
        "draft" => WorkItemStatus.InProgress,
        "in_progress" or "in progress" => WorkItemStatus.InProgress,
        "in_review" or "in review" => WorkItemStatus.InVerify,
        "open" or _ => WorkItemStatus.Todo
    };

    public static string[] MapTypeToGitHubLabels(WorkItemType type) => type switch
    {
        WorkItemType.Bug => new[] { "bug" },
        WorkItemType.Story => new[] { "enhancement" },
        _ => new[] { "task" }
    };

    public static WorkItemType MapTypeFromGitHubLabels(IEnumerable<string> labels, bool isPullRequest = false)
    {
        foreach (var label in labels)
        {
            var normalized = label.Trim().ToLowerInvariant();
            if (normalized.Contains("bug")) return WorkItemType.Bug;
            if (normalized.Contains("enhancement") || normalized.Contains("feature") || normalized.Contains("story")) return WorkItemType.Story;
        }
        return isPullRequest ? WorkItemType.Story : WorkItemType.Task;
    }
}
