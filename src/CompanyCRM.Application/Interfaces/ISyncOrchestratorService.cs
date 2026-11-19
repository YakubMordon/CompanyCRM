namespace CompanyCRM.Application.Interfaces;

using CompanyCRM.Application.DTOs;
using CompanyCRM.Domain.Enums;

public interface ISyncOrchestratorService
{
    /// <summary>Pushes an approved work item to the configured external issue tracker.</summary>
    Task<SyncResultDto> PushWorkItemToExternalAsync(Guid workItemId, CancellationToken ct = default);

    /// <summary>Processes an inbound webhook payload with echo-loop detection and conflict handling.</summary>
    Task<WebhookProcessingResultDto> ProcessInboundWebhookAsync(ExternalSourceType source, string rawPayload, IDictionary<string, string> headers, CancellationToken ct = default);

    /// <summary>Executes background reconciliation polling remote trackers for delta changes.</summary>
    Task<ReconciliationResultDto> RunReconciliationAsync(Guid? projectId = null, CancellationToken ct = default);

    /// <summary>Retrieves all unresolved synchronization conflicts for a project.</summary>
    Task<IReadOnlyList<SyncConflictDto>> GetPendingConflictsAsync(Guid? projectId = null, CancellationToken ct = default);

    /// <summary>Applies a user resolution action (Merge, KeepBoth, Skip) to a sync conflict.</summary>
    Task<ConflictResolutionResultDto> ResolveConflictAsync(Guid conflictId, ConflictResolutionAction action, CancellationToken ct = default);
}
