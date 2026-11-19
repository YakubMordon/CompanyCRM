using System.Collections.Concurrent;
using System.Text.Json;
using CompanyCRM.Application.DTOs;
using CompanyCRM.Domain.Entities;
using CompanyCRM.Domain.ValueObjects;

namespace CompanyCRM.Infrastructure.ExternalSync;

public class AzureDevOpsSimulator
{
    private readonly ConcurrentDictionary<string, ExternalWorkItemDto> _simulatedStore = new();

    public AzureDevOpsSimulator()
    {
        SeedDefaultItems();
    }

    public void Reset()
    {
        _simulatedStore.Clear();
        SeedDefaultItems();
    }

    private void SeedDefaultItems()
    {
        _simulatedStore["ADO-SIM-201"] = new ExternalWorkItemDto(
            ExternalId: "ADO-SIM-201",
            ExternalUrl: "https://dev.azure.com/simulated-org/CompanyProject/_workitems/edit/ADO-SIM-201",
            Title: "Refactor user profile avatar upload",
            Description: "Implement image compression and S3 bucket upload for avatars.",
            Status: "Active",
            Type: "Task",
            AssigneeEmail: "sarah.jenkins@companycrm.local",
            LastModifiedAt: DateTimeOffset.UtcNow.AddHours(-2));

        _simulatedStore["ADO-SIM-202"] = new ExternalWorkItemDto(
            ExternalId: "ADO-SIM-202",
            ExternalUrl: "https://dev.azure.com/simulated-org/CompanyProject/_workitems/edit/ADO-SIM-202",
            Title: "Optimize database index on WorkItems",
            Description: "Add composite indexes on ProjectId and Status for high-concurrency boards.",
            Status: "New",
            Type: "Task",
            AssigneeEmail: "alex.rivera@companycrm.local",
            LastModifiedAt: DateTimeOffset.UtcNow.AddHours(-4));

        _simulatedStore["ADO-SIM-203"] = new ExternalWorkItemDto(
            ExternalId: "ADO-SIM-203",
            ExternalUrl: "https://dev.azure.com/simulated-org/CompanyProject/_workitems/edit/ADO-SIM-203",
            Title: "Fix time tracking duration roundoff error",
            Description: "Time tracking sometimes rounds down when stopping timer within 30 seconds.",
            Status: "Resolved",
            Type: "Bug",
            AssigneeEmail: "maya.patel@companycrm.local",
            LastModifiedAt: DateTimeOffset.UtcNow.AddMinutes(-30));
    }

    public Task<ExternalWorkItemDto> PushWorkItemAsync(
        ProjectExternalConfig config,
        WorkItem workItem,
        CancellationToken ct = default)
    {
        string externalId;
        if (string.IsNullOrWhiteSpace(workItem.ExternalId))
        {
            var cleanedCode = string.IsNullOrWhiteSpace(workItem.Code)
                ? Guid.NewGuid().ToString("N")[..8]
                : workItem.Code.Replace("-", "");
            externalId = $"ADO-SIM-{cleanedCode}";
        }
        else
        {
            externalId = workItem.ExternalId;
        }

        var projectName = config.ProjectName ?? "CompanyProject";
        var externalUrl = $"https://dev.azure.com/simulated-org/{projectName}/_workitems/edit/{externalId}";

        var dto = new ExternalWorkItemDto(
            ExternalId: externalId,
            ExternalUrl: externalUrl,
            Title: workItem.Title,
            Description: workItem.Description,
            Status: AzureDevOpsIssueTrackerProvider.MapStatusToAdoState(workItem.Status),
            Type: AzureDevOpsIssueTrackerProvider.MapTypeToAdo(workItem.Type),
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
                $"https://dev.azure.com/simulated-org/CompanyProject/_workitems/edit/{externalId}",
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

    public string GenerateServiceHookPayload(ExternalWorkItemDto item, string eventType = "workitem.updated")
    {
        var payloadObj = new
        {
            subscriptionId = "simulated-subscription-uuid",
            notificationId = 1,
            id = Guid.NewGuid().ToString(),
            eventType = eventType,
            publisherId = "tfs",
            message = new { text = $"Work item {item.ExternalId} was updated in Azure DevOps." },
            resource = new
            {
                id = item.ExternalId,
                workItemId = item.ExternalId,
                rev = 2,
                url = item.ExternalUrl,
                _links = new
                {
                    html = new { href = item.ExternalUrl }
                },
                fields = new Dictionary<string, object?>
                {
                    ["System.Id"] = item.ExternalId,
                    ["System.Title"] = item.Title,
                    ["System.Description"] = item.Description ?? string.Empty,
                    ["System.State"] = item.Status,
                    ["System.WorkItemType"] = item.Type,
                    ["System.AssignedTo"] = item.AssigneeEmail ?? string.Empty,
                    ["System.ChangedDate"] = (item.LastModifiedAt ?? DateTimeOffset.UtcNow).ToString("o")
                }
            }
        };

        return JsonSerializer.Serialize(payloadObj);
    }
}
