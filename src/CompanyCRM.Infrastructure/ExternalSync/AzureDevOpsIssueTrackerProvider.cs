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

public class AzureDevOpsIssueTrackerProvider : IIssueTrackerProvider
{
    private readonly HttpClient _httpClient;
    private readonly AzureDevOpsSimulator _simulator;
    private readonly ILogger<AzureDevOpsIssueTrackerProvider> _logger;

    public ExternalSourceType SourceType => ExternalSourceType.AzureDevOps;

    public AzureDevOpsIssueTrackerProvider(
        HttpClient httpClient,
        AzureDevOpsSimulator simulator,
        ILogger<AzureDevOpsIssueTrackerProvider> logger)
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
        // 1. Sandbox Fallback when simulated or without PAT
        if (config.IsSimulatedMode || string.IsNullOrWhiteSpace(config.Pat))
        {
            _logger.LogInformation("Routing PushWorkItem to AzureDevOpsSimulator for {WorkItemCode}", workItem.Code);
            return await _simulator.PushWorkItemAsync(config, workItem, ct);
        }

        // 2. Prepare Live Azure DevOps REST API v7.1 JSON Patch Call
        var orgUrl = config.OrgUrl?.TrimEnd('/') ?? throw new ArgumentException("OrgUrl is required for live sync");
        var projectName = Uri.EscapeDataString(config.ProjectName ?? string.Empty);
        var authHeader = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($":{config.Pat}")));

        var adoType = MapTypeToAdo(workItem.Type);
        var patchOps = new List<object>
        {
            new { op = "add", path = "/fields/System.Title", value = workItem.Title },
            new { op = "add", path = "/fields/System.Description", value = workItem.Description ?? string.Empty },
            new { op = "add", path = "/fields/System.State", value = MapStatusToAdoState(workItem.Status) },
            new { op = "add", path = "/fields/Microsoft.VSTS.Common.Priority", value = MapPriorityToAdo(workItem.Priority) }
        };

        if (!string.IsNullOrWhiteSpace(workItem.Assignee?.Email))
        {
            patchOps.Add(new { op = "add", path = "/fields/System.AssignedTo", value = workItem.Assignee.Email });
        }

        HttpRequestMessage request;
        if (string.IsNullOrWhiteSpace(workItem.ExternalId))
        {
            var requestUri = $"{orgUrl}/{projectName}/_apis/wit/workitems/${Uri.EscapeDataString(adoType)}?api-version=7.1";
            request = new HttpRequestMessage(HttpMethod.Post, requestUri);
        }
        else
        {
            var requestUri = $"{orgUrl}/{projectName}/_apis/wit/workitems/{Uri.EscapeDataString(workItem.ExternalId)}?api-version=7.1";
            request = new HttpRequestMessage(HttpMethod.Patch, requestUri);
        }

        request.Headers.Authorization = authHeader;
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = new StringContent(JsonSerializer.Serialize(patchOps), Encoding.UTF8, "application/json-patch+json");

        var response = await _httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("Azure DevOps API error ({StatusCode}): {Body}", response.StatusCode, errorBody);
            throw new HttpRequestException($"Azure DevOps API error: {response.StatusCode} - {errorBody}");
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        return ParseAdoWorkItemJson(json);
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

        var orgUrl = config.OrgUrl?.TrimEnd('/') ?? throw new ArgumentException("OrgUrl is required for live sync");
        var projectName = Uri.EscapeDataString(config.ProjectName ?? string.Empty);
        var authHeader = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($":{config.Pat}")));

        // 1. WIQL Query to get changed IDs
        string wiql;
        if (modifiedSince.HasValue)
        {
            var dateIso = modifiedSince.Value.ToString("yyyy-MM-ddTHH:mm:ssZ");
            wiql = $"SELECT [System.Id], [System.ChangedDate] FROM WorkItems WHERE [System.TeamProject] = '{config.ProjectName}' AND [System.ChangedDate] >= '{dateIso}' ORDER BY [System.ChangedDate] DESC";
        }
        else
        {
            wiql = $"SELECT [System.Id], [System.ChangedDate] FROM WorkItems WHERE [System.TeamProject] = '{config.ProjectName}' ORDER BY [System.ChangedDate] DESC";
        }

        var wiqlUri = $"{orgUrl}/{projectName}/_apis/wit/wiql?api-version=7.1";
        var wiqlReq = new HttpRequestMessage(HttpMethod.Post, wiqlUri)
        {
            Headers = { Authorization = authHeader },
            Content = new StringContent(JsonSerializer.Serialize(new { query = wiql }), Encoding.UTF8, "application/json")
        };

        var wiqlResp = await _httpClient.SendAsync(wiqlReq, ct);
        if (!wiqlResp.IsSuccessStatusCode)
        {
            var err = await wiqlResp.Content.ReadAsStringAsync(ct);
            _logger.LogError("Azure DevOps WIQL query failed: {Err}", err);
            return Array.Empty<ExternalWorkItemDto>();
        }

        var wiqlJson = await wiqlResp.Content.ReadAsStringAsync(ct);
        using var wiqlDoc = JsonDocument.Parse(wiqlJson);
        var workItemsArray = wiqlDoc.RootElement.GetProperty("workItems");
        var ids = new List<long>();
        foreach (var item in workItemsArray.EnumerateArray())
        {
            if (item.TryGetProperty("id", out var idProp))
            {
                ids.Add(idProp.GetInt64());
            }
        }

        if (ids.Count == 0) return Array.Empty<ExternalWorkItemDto>();

        // 2. Batch retrieve work items details (up to 200)
        var idsParam = string.Join(",", ids.Take(200));
        var batchUri = $"{orgUrl}/{projectName}/_apis/wit/workitems?ids={idsParam}&$expand=all&api-version=7.1";
        var batchReq = new HttpRequestMessage(HttpMethod.Get, batchUri) { Headers = { Authorization = authHeader } };

        var batchResp = await _httpClient.SendAsync(batchReq, ct);
        if (!batchResp.IsSuccessStatusCode) return Array.Empty<ExternalWorkItemDto>();

        var batchJson = await batchResp.Content.ReadAsStringAsync(ct);
        using var batchDoc = JsonDocument.Parse(batchJson);
        var results = new List<ExternalWorkItemDto>();
        foreach (var item in batchDoc.RootElement.GetProperty("value").EnumerateArray())
        {
            results.Add(ParseAdoWorkItemElement(item));
        }

        return results;
    }

    public Task<ExternalWorkItemDto?> ParseWebhookPayloadAsync(
        string rawJson,
        IDictionary<string, string> headers,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawJson)) return Task.FromResult<ExternalWorkItemDto?>(null);

        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return Task.FromResult<ExternalWorkItemDto?>(null);

            if (!root.TryGetProperty("resource", out var resource) || resource.ValueKind != JsonValueKind.Object)
            {
                return Task.FromResult<ExternalWorkItemDto?>(null);
            }

            string? externalId = null;
            if (resource.TryGetProperty("id", out var idProp))
            {
                if (idProp.ValueKind == JsonValueKind.String) externalId = idProp.GetString();
                else if (idProp.ValueKind == JsonValueKind.Number) externalId = idProp.GetRawText();
            }
            else if (resource.TryGetProperty("workItemId", out var wIdProp))
            {
                if (wIdProp.ValueKind == JsonValueKind.String) externalId = wIdProp.GetString();
                else if (wIdProp.ValueKind == JsonValueKind.Number) externalId = wIdProp.GetRawText();
            }

            if (string.IsNullOrWhiteSpace(externalId))
            {
                return Task.FromResult<ExternalWorkItemDto?>(null);
            }

            string externalUrl = string.Empty;
            if (resource.TryGetProperty("_links", out var links) &&
                links.ValueKind == JsonValueKind.Object &&
                links.TryGetProperty("html", out var html) &&
                html.ValueKind == JsonValueKind.Object &&
                html.TryGetProperty("href", out var hrefProp) &&
                hrefProp.ValueKind == JsonValueKind.String)
            {
                externalUrl = hrefProp.GetString() ?? string.Empty;
            }
            else if (resource.TryGetProperty("url", out var urlProp) &&
                     urlProp.ValueKind == JsonValueKind.String)
            {
                externalUrl = urlProp.GetString() ?? string.Empty;
            }

            JsonElement fieldsElement = default;
            if (resource.TryGetProperty("fields", out var f) && f.ValueKind == JsonValueKind.Object)
            {
                fieldsElement = f;
            }
            else if (resource.TryGetProperty("revision", out var rev) &&
                     rev.ValueKind == JsonValueKind.Object &&
                     rev.TryGetProperty("fields", out var revFields) &&
                     revFields.ValueKind == JsonValueKind.Object)
            {
                fieldsElement = revFields;
            }

            string title = string.Empty;
            string? description = null;
            string status = "To Do";
            string type = "Task";
            string? assigneeEmail = null;
            DateTimeOffset? changedDate = null;

            if (fieldsElement.ValueKind == JsonValueKind.Object)
            {
                title = GetSafeString(fieldsElement, "System.Title") ?? string.Empty;
                description = GetSafeString(fieldsElement, "System.Description");
                status = GetSafeString(fieldsElement, "System.State") ?? "To Do";
                type = GetSafeString(fieldsElement, "System.WorkItemType") ?? "Task";

                if (fieldsElement.TryGetProperty("System.ChangedDate", out var cdProp) &&
                    cdProp.ValueKind == JsonValueKind.String &&
                    cdProp.TryGetDateTimeOffset(out var cd))
                {
                    changedDate = cd;
                }

                if (fieldsElement.TryGetProperty("System.AssignedTo", out var aProp))
                {
                    if (aProp.ValueKind == JsonValueKind.String)
                    {
                        assigneeEmail = aProp.GetString();
                    }
                    else if (aProp.ValueKind == JsonValueKind.Object)
                    {
                        assigneeEmail = GetSafeString(aProp, "uniqueName") ?? GetSafeString(aProp, "mail");
                    }
                }
            }

            return Task.FromResult<ExternalWorkItemDto?>(new ExternalWorkItemDto(
                ExternalId: externalId,
                ExternalUrl: externalUrl,
                Title: title,
                Description: description,
                Status: status,
                Type: type,
                AssigneeEmail: assigneeEmail,
                LastModifiedAt: changedDate ?? DateTimeOffset.UtcNow));
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Failed to parse Azure DevOps Service Hook webhook JSON payload");
            return Task.FromResult<ExternalWorkItemDto?>(null);
        }
    }

    private static string? GetSafeString(JsonElement obj, string propertyName)
    {
        if (obj.ValueKind != JsonValueKind.Object) return null;
        if (!obj.TryGetProperty(propertyName, out var prop)) return null;
        return prop.ValueKind == JsonValueKind.String ? prop.GetString() : null;
    }

    private static ExternalWorkItemDto ParseAdoWorkItemJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return ParseAdoWorkItemElement(doc.RootElement);
    }

    private static ExternalWorkItemDto ParseAdoWorkItemElement(JsonElement item)
    {
        var id = item.GetProperty("id").GetInt64().ToString();
        var url = string.Empty;
        if (item.TryGetProperty("_links", out var links) &&
            links.TryGetProperty("html", out var html) &&
            html.TryGetProperty("href", out var href))
        {
            url = href.GetString() ?? string.Empty;
        }

        var fields = item.GetProperty("fields");
        var title = fields.GetProperty("System.Title").GetString() ?? string.Empty;
        var description = fields.TryGetProperty("System.Description", out var d) ? d.GetString() : null;
        var state = fields.GetProperty("System.State").GetString() ?? "New";
        var type = fields.GetProperty("System.WorkItemType").GetString() ?? "Task";
        string? email = null;
        if (fields.TryGetProperty("System.AssignedTo", out var a))
        {
            if (a.ValueKind == JsonValueKind.String) email = a.GetString();
            else if (a.ValueKind == JsonValueKind.Object && a.TryGetProperty("uniqueName", out var un)) email = un.GetString();
        }

        DateTimeOffset? changed = null;
        if (fields.TryGetProperty("System.ChangedDate", out var cd) && cd.TryGetDateTimeOffset(out var dto)) changed = dto;

        return new ExternalWorkItemDto(id, url, title, description, state, type, email, changed);
    }

    public static string MapStatusToAdoState(WorkItemStatus status) => status switch
    {
        WorkItemStatus.Draft or WorkItemStatus.Backlog => "New",
        WorkItemStatus.Todo => "To Do",
        WorkItemStatus.InProgress => "Active",
        WorkItemStatus.InVerify => "Resolved",
        WorkItemStatus.Done => "Closed",
        _ => "To Do"
    };

    public static WorkItemStatus MapAdoStateToStatus(string? state) => (state?.Trim().ToLowerInvariant()) switch
    {
        "closed" or "done" => WorkItemStatus.Done,
        "resolved" or "in verify" => WorkItemStatus.InVerify,
        "active" or "in progress" => WorkItemStatus.InProgress,
        "new" or "backlog" => WorkItemStatus.Backlog,
        "to do" or "todo" => WorkItemStatus.Todo,
        _ => WorkItemStatus.Todo
    };

    public static string MapTypeToAdo(WorkItemType type) => type switch
    {
        WorkItemType.Bug => "Bug",
        WorkItemType.Story => "User Story",
        _ => "Task"
    };

    public static WorkItemType MapAdoTypeToType(string? type) => (type?.Trim().ToLowerInvariant()) switch
    {
        "bug" => WorkItemType.Bug,
        "user story" or "story" or "product backlog item" => WorkItemType.Story,
        _ => WorkItemType.Task
    };

    public static int MapPriorityToAdo(WorkItemPriority priority) => priority switch
    {
        WorkItemPriority.Critical => 1,
        WorkItemPriority.High => 2,
        WorkItemPriority.Medium => 3,
        WorkItemPriority.Low => 4,
        _ => 3
    };
}
