using CompanyCRM.Api.Hubs;
using CompanyCRM.Application.DTOs;
using CompanyCRM.Application.Interfaces;
using CompanyCRM.Domain.Entities;
using CompanyCRM.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace CompanyCRM.Api.Controllers;

[ApiController]
public class SyncController : ControllerBase
{
    private readonly ISyncOrchestratorService _syncOrchestrator;
    private readonly IHubContext<CrmHub, ICrmHubClient> _hubContext;
    private readonly IWorkItemRepository _workItemRepository;

    public SyncController(
        ISyncOrchestratorService syncOrchestrator,
        IHubContext<CrmHub, ICrmHubClient> hubContext,
        IWorkItemRepository workItemRepository)
    {
        _syncOrchestrator = syncOrchestrator;
        _hubContext = hubContext;
        _workItemRepository = workItemRepository;
    }

    [HttpPost("api/webhooks/ado")]
    [AllowAnonymous]
    public Task<ActionResult<WebhookProcessingResultDto>> ProcessAdoWebhook(CancellationToken ct) =>
        HandleInboundWebhookAsync(ExternalSourceType.AzureDevOps, ct);

    [HttpPost("api/webhooks/jira")]
    [AllowAnonymous]
    public Task<ActionResult<WebhookProcessingResultDto>> ProcessJiraWebhook(CancellationToken ct) =>
        HandleInboundWebhookAsync(ExternalSourceType.Jira, ct);

    [HttpPost("api/webhooks/github")]
    [AllowAnonymous]
    public Task<ActionResult<WebhookProcessingResultDto>> ProcessGitHubWebhook(CancellationToken ct) =>
        HandleInboundWebhookAsync(ExternalSourceType.GitHub, ct);

    private async Task<ActionResult<WebhookProcessingResultDto>> HandleInboundWebhookAsync(
        ExternalSourceType source,
        CancellationToken ct)
    {
        using var reader = new StreamReader(Request.Body);
        var rawPayload = await reader.ReadToEndAsync(ct);

        if (string.IsNullOrWhiteSpace(rawPayload))
        {
            return BadRequest(new WebhookProcessingResultDto("Error", null, null, false, "Empty payload"));
        }

        var headers = Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString());

        var result = await _syncOrchestrator.ProcessInboundWebhookAsync(source, rawPayload, headers, ct);

        if (result.Status == "ConflictDetected")
        {
            await BroadcastConflictEventAsync(result);
        }
        else if (!result.IsEcho && result.Status == "Applied")
        {
            await BroadcastAppliedEventAsync(result, ct);
        }

        return Ok(result);
    }

    private async Task BroadcastConflictEventAsync(WebhookProcessingResultDto result)
    {
        var conflictProjectId = result.Conflict?.ProjectId ?? Guid.Empty;
        await _hubContext.Clients.Groups(CrmHub.ProjectAndUnscopedGroups(conflictProjectId)).SyncStatusChanged(new SyncEventDto(
            conflictProjectId,
            result.WorkItemId,
            "ConflictDetected",
            result.Message,
            result.ExternalId,
            DateTimeOffset.UtcNow
        ));
    }

    private async Task BroadcastAppliedEventAsync(WebhookProcessingResultDto result, CancellationToken ct)
    {
        var workItemDto = result.WorkItemId.HasValue
            ? await LoadWorkItemDtoAsync(result.WorkItemId.Value, ct)
            : null;
        var projectId = workItemDto?.ProjectId ?? Guid.Empty;
        var syncStatus = result.Message.Contains("Imported", StringComparison.OrdinalIgnoreCase)
            ? "WorkItemImported"
            : "SyncCompleted";

        var groups = CrmHub.ProjectAndUnscopedGroups(projectId);
        await _hubContext.Clients.Groups(groups).SyncStatusChanged(new SyncEventDto(
            projectId,
            result.WorkItemId,
            syncStatus,
            result.Message,
            result.ExternalId,
            DateTimeOffset.UtcNow
        ));

        if (workItemDto != null)
        {
            await _hubContext.Clients.Groups(groups).WorkItemUpdated(workItemDto);
        }
    }

    [HttpPost("api/sync/projects/{id:guid}/reconcile")]
    [Authorize]
    public async Task<ActionResult<ReconciliationResultDto>> ReconcileProject([FromRoute] Guid id, CancellationToken ct)
    {
        var result = await _syncOrchestrator.RunReconciliationAsync(id, ct);

        await _hubContext.Clients.Groups(CrmHub.ProjectAndUnscopedGroups(id)).SyncStatusChanged(new SyncEventDto(
            id,
            null,
            "SyncCompleted",
            $"Reconciliation completed. Updated: {result.UpdatedCount}, Conflicts: {result.ConflictsCount}.",
            null,
            DateTimeOffset.UtcNow
        ));

        return Ok(result);
    }

    [HttpPost("api/sync/workitems/{id:guid}/push")]
    [Authorize]
    public async Task<ActionResult<SyncResultDto>> PushWorkItem([FromRoute] Guid id, CancellationToken ct)
    {
        try
        {
            var result = await _syncOrchestrator.PushWorkItemToExternalAsync(id, ct);
            if (!result.Success)
            {
                return BadRequest(result);
            }

            var workItemDto = await LoadWorkItemDtoAsync(id, ct);
            var projectId = workItemDto?.ProjectId ?? Guid.Empty;
            var groups = CrmHub.ProjectAndUnscopedGroups(projectId);

            await _hubContext.Clients.Groups(groups).SyncStatusChanged(new SyncEventDto(
                projectId,
                id,
                "WorkItemPushed",
                $"Work item pushed to {result.ExternalId}.",
                result.ExternalId,
                DateTimeOffset.UtcNow
            ));

            if (workItemDto != null)
            {
                await _hubContext.Clients.Groups(groups).WorkItemUpdated(workItemDto);
            }

            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpGet("api/sync/conflicts")]
    [Authorize]
    public async Task<ActionResult<IReadOnlyList<SyncConflictDto>>> GetConflicts([FromQuery] Guid? projectId, CancellationToken ct)
    {
        var conflicts = await _syncOrchestrator.GetPendingConflictsAsync(projectId, ct);
        return Ok(conflicts);
    }

    [HttpPost("api/sync/conflicts/{id:guid}/resolve")]
    [Authorize]
    public async Task<ActionResult<ConflictResolutionResultDto>> ResolveConflict(
        [FromRoute] Guid id,
        [FromBody] ResolveConflictRequestDto request,
        CancellationToken ct)
    {
        if (request.Action == ConflictResolutionAction.Unresolved)
        {
            return BadRequest(new { message = "Action must be Merge, KeepBoth, or Skip." });
        }

        try
        {
            var result = await _syncOrchestrator.ResolveConflictAsync(id, request.Action, ct);
            if (!result.Resolved)
            {
                return BadRequest(result);
            }

            var workItemDto = result.ResolvedWorkItemId.HasValue
                ? await LoadWorkItemDtoAsync(result.ResolvedWorkItemId.Value, ct)
                : null;
            var projectId = workItemDto?.ProjectId
                ?? await _workItemRepository.GetConflictProjectIdAsync(id, ct)
                ?? Guid.Empty;

            var groups = CrmHub.ProjectAndUnscopedGroups(projectId);
            await _hubContext.Clients.Groups(groups).SyncStatusChanged(new SyncEventDto(
                projectId,
                result.ResolvedWorkItemId,
                "SyncCompleted",
                result.Message ?? $"Conflict resolved with {request.Action}.",
                null,
                DateTimeOffset.UtcNow
            ));

            if (workItemDto != null)
            {
                await _hubContext.Clients.Groups(groups).WorkItemUpdated(workItemDto);
            }

            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    private async Task<WorkItemDto?> LoadWorkItemDtoAsync(Guid workItemId, CancellationToken ct)
    {
        var w = await _workItemRepository.GetByIdWithDetailsAsync(workItemId, trackChanges: false, ct);

        if (w == null) return null;

        return MapToWorkItemDto(w);
    }

    private static WorkItemDto MapToWorkItemDto(WorkItem w)
    {
        var totalLoggedMinutes = w.TimeEntries?.Where(t => !t.IsRunning).Sum(t => t.DurationMinutes) ?? 0;
        var billableMinutes = w.TimeEntries?.Where(t => !t.IsRunning && t.IsBillable).Sum(t => t.DurationMinutes) ?? 0;

        return new WorkItemDto(
            w.Id,
            w.ProjectId,
            w.Project?.Code ?? "CRM",
            w.Code,
            w.Title,
            w.Description,
            w.Type,
            w.Status,
            w.Priority,
            w.EstimatedHours,
            w.StoryPoints,
            w.SprintName,
            w.BoardPosition,
            w.CreatedById,
            w.CreatedBy?.FullName ?? "Unknown",
            w.AssigneeId,
            w.Assignee?.FullName,
            w.Assignee?.Email,
            w.ReviewerId,
            w.Reviewer?.FullName,
            w.ReviewStatus,
            w.ReviewLatestComment,
            w.ReviewedAt,
            w.ExternalSource,
            w.ExternalId,
            w.ExternalUrl,
            w.LastSyncedAt,
            w.LastSyncedContentHash,
            w.LastSyncOrigin,
            totalLoggedMinutes,
            billableMinutes,
            false,
            w.CreatedAt,
            w.UpdatedAt
        );
    }
}
