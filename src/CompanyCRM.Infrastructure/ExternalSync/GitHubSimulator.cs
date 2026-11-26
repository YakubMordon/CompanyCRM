using System.Collections.Concurrent;
using CompanyCRM.Application.DTOs;
using CompanyCRM.Domain.Entities;
using CompanyCRM.Domain.ValueObjects;

namespace CompanyCRM.Infrastructure.ExternalSync;

public class GitHubSimulator
{
    private readonly ConcurrentDictionary<string, ExternalWorkItemDto> _simulatedStore = new();
    private int _issueNumberCounter = 103;

    public GitHubSimulator()
    {
        SeedDefaultItems();
    }

    public void Reset()
    {
        _simulatedStore.Clear();
        _issueNumberCounter = 103;
        SeedDefaultItems();
    }

    private void SeedDefaultItems()
    {
        _simulatedStore["GH-101"] = new ExternalWorkItemDto(
            ExternalId: "GH-101",
            ExternalUrl: "https://github.com/companycrm/platform/issues/101",
            Title: "Add GitHub Issues and Pull Requests bidirectional sync",
            Description: "Implement GitHub REST API v3 provider with webhook support and simulated sandbox mode.",
            Status: "open",
            Type: "Task",
            AssigneeEmail: "alex.rivera@companycrm.local",
            LastModifiedAt: DateTimeOffset.UtcNow.AddHours(-3));

        _simulatedStore["GH-102"] = new ExternalWorkItemDto(
            ExternalId: "GH-102",
            ExternalUrl: "https://github.com/companycrm/platform/issues/102",
            Title: "Fix SignalR reconnect race condition on token refresh",
            Description: "Ensure WebSocket hub reconnects cleanly when JWT access token is rotated.",
            Status: "open",
            Type: "Bug",
            AssigneeEmail: "maya.patel@companycrm.local",
            LastModifiedAt: DateTimeOffset.UtcNow.AddHours(-1));

        _simulatedStore["GH-PR-103"] = new ExternalWorkItemDto(
            ExternalId: "GH-PR-103",
            ExternalUrl: "https://github.com/companycrm/platform/pull/103",
            Title: "feat: upgrade EF Core and Aspire packages to latest stable",
            Description: "Pull request upgrading NuGet dependencies and verifying integration tests.",
            Status: "merged",
            Type: "Story",
            AssigneeEmail: "sarah.jenkins@companycrm.local",
            LastModifiedAt: DateTimeOffset.UtcNow.AddMinutes(-30));
    }

    public Task<ExternalWorkItemDto> PushWorkItemAsync(
        ProjectExternalConfig config,
        WorkItem workItem,
        CancellationToken ct = default)
    {
        var repo = !string.IsNullOrWhiteSpace(config.ProjectName)
            ? config.ProjectName.Trim().Trim('/')
            : "companycrm/platform";

        if (!repo.Contains('/'))
        {
            repo = $"companycrm/{repo}";
        }

        string externalId;
        int issueNumber;
        if (string.IsNullOrWhiteSpace(workItem.ExternalId))
        {
            issueNumber = Interlocked.Increment(ref _issueNumberCounter);
            externalId = $"GH-{issueNumber}";
        }
        else
        {
            externalId = workItem.ExternalId;
            var numberPart = externalId.Replace("GH-PR-", "", StringComparison.OrdinalIgnoreCase)
                                       .Replace("GH-", "", StringComparison.OrdinalIgnoreCase);
            issueNumber = int.TryParse(numberPart, out var parsed) ? parsed : Interlocked.Increment(ref _issueNumberCounter);
        }

        var isPr = externalId.StartsWith("GH-PR-", StringComparison.OrdinalIgnoreCase);
        var pathSegment = isPr ? "pull" : "issues";
        var externalUrl = $"https://github.com/{repo}/{pathSegment}/{issueNumber}";

        var dto = new ExternalWorkItemDto(
            ExternalId: externalId,
            ExternalUrl: externalUrl,
            Title: workItem.Title,
            Description: workItem.Description,
            Status: GitHubIssueTrackerProvider.MapStatusToGitHub(workItem.Status),
            Type: workItem.Type.ToString(),
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
            items = items.Where(i => i.LastModifiedAt >= modifiedSince.Value);
        }

        return Task.FromResult<IReadOnlyList<ExternalWorkItemDto>>(items.ToList());
    }
}
