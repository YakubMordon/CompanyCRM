using System.Collections.Concurrent;
using CompanyCRM.Application.DTOs;
using CompanyCRM.Domain.Entities;
using CompanyCRM.Domain.ValueObjects;

namespace CompanyCRM.Infrastructure.ExternalSync;

public class JiraSimulator
{
    private readonly ConcurrentDictionary<string, ExternalWorkItemDto> _simulatedStore = new();
    private int _keyCounter = 100;

    public JiraSimulator()
    {
        SeedDefaultItems();
    }

    public void Reset()
    {
        _simulatedStore.Clear();
        _keyCounter = 100;
        SeedDefaultItems();
    }

    private void SeedDefaultItems()
    {
        _simulatedStore["JIRA-SIM-101"] = new ExternalWorkItemDto(
            ExternalId: "JIRA-SIM-101",
            ExternalUrl: "https://simulated-jira.atlassian.net/browse/JIRA-SIM-101",
            Title: "Migrate auth tokens to OAuth 2.0 PKCE",
            Description: "Transition legacy API key authentication to OAuth 2.0 authorization code flow with PKCE.",
            Status: "In Progress",
            Type: "Task",
            AssigneeEmail: "sarah.jenkins@companycrm.local",
            LastModifiedAt: DateTimeOffset.UtcNow.AddHours(-2));

        _simulatedStore["JIRA-SIM-102"] = new ExternalWorkItemDto(
            ExternalId: "JIRA-SIM-102",
            ExternalUrl: "https://simulated-jira.atlassian.net/browse/JIRA-SIM-102",
            Title: "Implement rate limiting middleware on API gateway",
            Description: "Enforce IP and tenant based sliding window token bucket limits.",
            Status: "To Do",
            Type: "Story",
            AssigneeEmail: "alex.rivera@companycrm.local",
            LastModifiedAt: DateTimeOffset.UtcNow.AddHours(-5));

        _simulatedStore["JIRA-SIM-103"] = new ExternalWorkItemDto(
            ExternalId: "JIRA-SIM-103",
            ExternalUrl: "https://simulated-jira.atlassian.net/browse/JIRA-SIM-103",
            Title: "Fix timezone shift in recurring Hangfire schedule",
            Description: "Daylight saving boundary causes 1 hour premature dispatch of cutoff reminder.",
            Status: "Done",
            Type: "Bug",
            AssigneeEmail: "maya.patel@companycrm.local",
            LastModifiedAt: DateTimeOffset.UtcNow.AddMinutes(-45));
    }

    public Task<ExternalWorkItemDto> PushWorkItemAsync(
        ProjectExternalConfig config,
        WorkItem workItem,
        CancellationToken ct = default)
    {
        string externalId;
        if (string.IsNullOrWhiteSpace(workItem.ExternalId))
        {
            var projectKey = !string.IsNullOrWhiteSpace(config.ProjectName)
                ? config.ProjectName.Trim().ToUpperInvariant()
                : "JIRA";
            var nextId = Interlocked.Increment(ref _keyCounter);
            externalId = $"{projectKey}-{nextId}";
        }
        else
        {
            externalId = workItem.ExternalId;
        }

        var baseUrl = config.OrgUrl?.TrimEnd('/') ?? "https://simulated-jira.atlassian.net";
        var externalUrl = $"{baseUrl}/browse/{externalId}";

        var dto = new ExternalWorkItemDto(
            ExternalId: externalId,
            ExternalUrl: externalUrl,
            Title: workItem.Title,
            Description: workItem.Description,
            Status: JiraIssueTrackerProvider.MapStatusToJira(workItem.Status),
            Type: JiraIssueTrackerProvider.MapTypeToJira(workItem.Type),
            AssigneeEmail: workItem.Assignee?.Email,
            LastModifiedAt: DateTimeOffset.UtcNow);

        _simulatedStore[externalId] = dto;
        return Task.FromResult(dto);
    }

    public Task<IReadOnlyList<ExternalWorkItemDto>> FetchRemoteWorkItemsAsync(
        ProjectExternalConfig config,
        DateTimeOffset? modifiedSince = null,
        CancellationToken ct = default)
    {
        var items = _simulatedStore.Values.AsEnumerable();
        if (modifiedSince.HasValue)
        {
            items = items.Where(i => i.LastModifiedAt == null || i.LastModifiedAt >= modifiedSince.Value);
        }

        return Task.FromResult<IReadOnlyList<ExternalWorkItemDto>>(items.ToList());
    }

    public ExternalWorkItemDto SimulateRemoteModification(
        string externalId,
        string newTitle,
        string? newDesc,
        string newStatus,
        string? newEmail)
    {
        var existing = _simulatedStore.GetValueOrDefault(externalId)
            ?? new ExternalWorkItemDto(
                externalId,
                $"https://simulated-jira.atlassian.net/browse/{externalId}",
                newTitle,
                newDesc,
                newStatus,
                "Task",
                newEmail);

        var updated = existing with
        {
            Title = newTitle,
            Description = newDesc,
            Status = newStatus,
            AssigneeEmail = newEmail,
            LastModifiedAt = DateTimeOffset.UtcNow
        };

        _simulatedStore[externalId] = updated;
        return updated;
    }

    public string GenerateWebhookPayload(ExternalWorkItemDto item, string eventType = "jira:issue_updated")
    {
        var payloadObj = new
        {
            webhookEvent = eventType,
            timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            issue = new
            {
                id = item.ExternalId.GetHashCode().ToString(),
                key = item.ExternalId,
                self = $"{item.ExternalUrl}",
                fields = new
                {
                    summary = item.Title,
                    description = item.Description,
                    issuetype = new
                    {
                        name = item.Type
                    },
                    status = new
                    {
                        name = item.Status
                    },
                    priority = new
                    {
                        name = "Medium"
                    },
                    updated = (item.LastModifiedAt ?? DateTimeOffset.UtcNow).ToString("o"),
                    assignee = item.AssigneeEmail != null ? new
                    {
                        emailAddress = item.AssigneeEmail,
                        displayName = item.AssigneeEmail
                    } : null
                }
            }
        };

        return System.Text.Json.JsonSerializer.Serialize(payloadObj);
    }
}
