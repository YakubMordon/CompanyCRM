namespace CompanyCRM.Application.Interfaces;

using CompanyCRM.Application.DTOs;
using CompanyCRM.Domain.Entities;
using CompanyCRM.Domain.Enums;
using CompanyCRM.Domain.ValueObjects;

public interface IIssueTrackerProvider
{
    /// <summary>Identifies the target issue tracker (e.g. AzureDevOps).</summary>
    ExternalSourceType SourceType { get; }

    /// <summary>Pushes a CRM work item to the external tracker via REST API or sandbox engine.</summary>
    Task<ExternalWorkItemDto> PushWorkItemAsync(ProjectExternalConfig config, WorkItem workItem, CancellationToken ct = default);

    /// <summary>Queries remote work items modified since a given timestamp for background reconciliation.</summary>
    Task<IReadOnlyList<ExternalWorkItemDto>> FetchRemoteWorkItemsAsync(ProjectExternalConfig config, DateTimeOffset? modifiedSince = null, CancellationToken ct = default);

    /// <summary>Parses an inbound service hook webhook JSON payload into a standardized DTO.</summary>
    Task<ExternalWorkItemDto?> ParseWebhookPayloadAsync(string rawJson, IDictionary<string, string> headers, CancellationToken ct = default);
}
