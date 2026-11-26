using CompanyCRM.Application.DTOs;
using CompanyCRM.Application.Interfaces;
using CompanyCRM.Domain.Entities;
using CompanyCRM.Domain.Enums;
using CompanyCRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CompanyCRM.Infrastructure.Services;

public class WorkItemReviewService : IWorkItemReviewService
{
    private readonly CrmDbContext _dbContext;
    private readonly IEmailNotificationService _emailNotificationService;
    private readonly ISyncOrchestratorService _syncOrchestrator;
    private readonly IChatWebhookNotificationService? _chatWebhookService;

    public WorkItemReviewService(
        CrmDbContext dbContext,
        IEmailNotificationService emailNotificationService,
        ISyncOrchestratorService syncOrchestrator,
        IChatWebhookNotificationService? chatWebhookService = null)
    {
        _dbContext = dbContext;
        _emailNotificationService = emailNotificationService;
        _syncOrchestrator = syncOrchestrator;
        _chatWebhookService = chatWebhookService;
    }

    public async Task<WorkItemDto> AssignReviewerAsync(Guid workItemId, Guid reviewerId, CancellationToken ct = default)
    {
        var item = await _dbContext.WorkItems
            .Include(w => w.Project)
            .Include(w => w.CreatedBy)
            .Include(w => w.Assignee)
            .Include(w => w.Reviewer)
            .FirstOrDefaultAsync(w => w.Id == workItemId, ct)
            ?? throw new KeyNotFoundException($"Work item {workItemId} not found");

        var reviewer = await _dbContext.Employees.FindAsync(new object[] { reviewerId }, ct)
            ?? throw new KeyNotFoundException($"Reviewer employee {reviewerId} not found");

        item.AssignReviewer(reviewerId);
        item.Reviewer = reviewer;

        _dbContext.WorkItemHistories.Add(new WorkItemHistory
        {
            WorkItemId = item.Id,
            ChangedById = reviewerId,
            Action = "ReviewerAssigned",
            FieldName = "ReviewerId",
            NewValue = reviewer.FullName,
            Note = $"Assigned reviewer {reviewer.FullName} on creation.",
            ChangedAt = DateTimeOffset.UtcNow
        });

        await _dbContext.SaveChangesAsync(ct);

        await _emailNotificationService.SendReviewAssignedEmailAsync(
            reviewer.Email,
            reviewer.FullName,
            item.Code,
            item.Title,
            item.Id,
            ct);

        if (_chatWebhookService != null)
        {
            await _chatWebhookService.SendReviewAssignedAlertAsync(
                reviewer.FullName,
                item.Code,
                item.Title,
                item.Id,
                item.ProjectId,
                ct);
        }

        return ToDto(item);
    }

    public Task<WorkItemDto> ApproveReviewAsync(Guid workItemId, Guid reviewerId, string? approvalNote = null, CancellationToken ct = default)
        => ApproveReviewAsync(workItemId, reviewerId, WorkItemStatus.Todo, approvalNote, ct);

    public async Task<WorkItemDto> ApproveReviewAsync(Guid workItemId, Guid reviewerId, WorkItemStatus startingStatus, string? approvalNote = null, CancellationToken ct = default)
    {
        if (startingStatus != WorkItemStatus.Todo && startingStatus != WorkItemStatus.Backlog)
        {
            throw new ArgumentException("StartingStatus must be either Todo or Backlog.", nameof(startingStatus));
        }

        var item = await _dbContext.WorkItems
            .Include(w => w.Project)
            .Include(w => w.CreatedBy)
            .Include(w => w.Assignee)
            .Include(w => w.Reviewer)
            .FirstOrDefaultAsync(w => w.Id == workItemId, ct)
            ?? throw new KeyNotFoundException($"Work item {workItemId} not found");

        if (item.ReviewStatus == ReviewStatus.Denied)
        {
            throw new InvalidOperationException("Cannot approve a denied work item.");
        }

        if (item.ReviewStatus == ReviewStatus.Approved)
        {
            throw new InvalidOperationException("Work item review has already been approved.");
        }

        item.ApproveReview(reviewerId, startingStatus);
        if (!string.IsNullOrWhiteSpace(approvalNote))
        {
            item.ReviewLatestComment = approvalNote;
        }

        _dbContext.WorkItemHistories.Add(new WorkItemHistory
        {
            WorkItemId = item.Id,
            ChangedById = reviewerId,
            Action = "ReviewApproved",
            Note = approvalNote ?? $"Review approved; transitioned to {startingStatus}.",
            ChangedAt = DateTimeOffset.UtcNow
        });

        await _dbContext.SaveChangesAsync(ct);

        // Initiate outbound external push if project sync is enabled
        await _syncOrchestrator.PushWorkItemToExternalAsync(item.Id, ct);

        return ToDto(item);
    }

    public async Task<WorkItemDto> RequestClarificationAsync(Guid workItemId, Guid reviewerId, string clarificationComment, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(clarificationComment) || clarificationComment.Trim().Length < 5)
        {
            throw new ArgumentException("Clarification comment must contain at least 5 characters.", nameof(clarificationComment));
        }

        var item = await _dbContext.WorkItems
            .Include(w => w.Project)
            .Include(w => w.CreatedBy)
            .Include(w => w.Assignee)
            .Include(w => w.Reviewer)
            .FirstOrDefaultAsync(w => w.Id == workItemId, ct)
            ?? throw new KeyNotFoundException($"Work item {workItemId} not found");

        item.RequestClarification(reviewerId, clarificationComment.Trim());

        _dbContext.WorkItemHistories.Add(new WorkItemHistory
        {
            WorkItemId = item.Id,
            ChangedById = reviewerId,
            Action = "ReviewClarificationRequested",
            Note = clarificationComment.Trim(),
            ChangedAt = DateTimeOffset.UtcNow
        });

        await _dbContext.SaveChangesAsync(ct);

        var creator = item.CreatedBy ?? await _dbContext.Employees.FindAsync(new object[] { item.CreatedById }, ct);
        if (creator != null)
        {
            await _emailNotificationService.SendReviewDecisionEmailAsync(
                creator.Email,
                creator.FullName,
                item.Code,
                item.Title,
                ReviewStatus.ClarificationNeeded,
                clarificationComment.Trim(),
                ct);
        }

        return ToDto(item);
    }

    public async Task<WorkItemDto> DenyReviewAsync(Guid workItemId, Guid reviewerId, string denialReason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(denialReason) || denialReason.Trim().Length < 5)
        {
            throw new ArgumentException("Denial reason must contain at least 5 characters.", nameof(denialReason));
        }

        var item = await _dbContext.WorkItems
            .Include(w => w.Project)
            .Include(w => w.CreatedBy)
            .Include(w => w.Assignee)
            .Include(w => w.Reviewer)
            .FirstOrDefaultAsync(w => w.Id == workItemId, ct)
            ?? throw new KeyNotFoundException($"Work item {workItemId} not found");

        item.DenyReview(reviewerId, denialReason.Trim());

        _dbContext.WorkItemHistories.Add(new WorkItemHistory
        {
            WorkItemId = item.Id,
            ChangedById = reviewerId,
            Action = "ReviewDenied",
            Note = denialReason.Trim(),
            ChangedAt = DateTimeOffset.UtcNow
        });

        await _dbContext.SaveChangesAsync(ct);

        var creator = item.CreatedBy ?? await _dbContext.Employees.FindAsync(new object[] { item.CreatedById }, ct);
        if (creator != null)
        {
            await _emailNotificationService.SendReviewDecisionEmailAsync(
                creator.Email,
                creator.FullName,
                item.Code,
                item.Title,
                ReviewStatus.Denied,
                denialReason.Trim(),
                ct);
        }

        return ToDto(item);
    }

    private static WorkItemDto ToDto(WorkItem item)
    {
        return new WorkItemDto(
            item.Id,
            item.ProjectId,
            item.Project?.Code ?? "CRM",
            item.Code,
            item.Title,
            item.Description,
            item.Type,
            item.Status,
            item.Priority,
            item.EstimatedHours,
            item.StoryPoints,
            item.SprintName,
            item.BoardPosition,
            item.CreatedById,
            item.CreatedBy?.FullName ?? "Unknown",
            item.AssigneeId,
            item.Assignee?.FullName,
            item.Assignee?.Email,
            item.ReviewerId,
            item.Reviewer?.FullName,
            item.ReviewStatus,
            item.ReviewLatestComment,
            item.ReviewedAt,
            item.ExternalSource,
            item.ExternalId,
            item.ExternalUrl,
            item.LastSyncedAt,
            item.LastSyncedContentHash,
            item.LastSyncOrigin,
            0,
            0,
            false,
            item.CreatedAt,
            item.UpdatedAt
        );
    }
}
