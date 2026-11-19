namespace CompanyCRM.Application.Interfaces;

using CompanyCRM.Application.DTOs;
using CompanyCRM.Domain.Enums;

public interface IWorkItemReviewService
{
    /// <summary>Assigns a reviewer to a work item, transitions to PendingReview, and sends email notification.</summary>
    Task<WorkItemDto> AssignReviewerAsync(Guid workItemId, Guid reviewerId, CancellationToken ct = default);

    /// <summary>Approves a work item review, transitions to Todo (default), and initiates external outbound push.</summary>
    Task<WorkItemDto> ApproveReviewAsync(Guid workItemId, Guid reviewerId, string? approvalNote = null, CancellationToken ct = default);

    /// <summary>Approves a work item review, transitions to the specified starting status (Todo or Backlog), and initiates external outbound push.</summary>
    Task<WorkItemDto> ApproveReviewAsync(Guid workItemId, Guid reviewerId, WorkItemStatus startingStatus, string? approvalNote = null, CancellationToken ct = default);

    /// <summary>Requests clarification on a work item; validates comment >= 5 chars, transitions to ClarificationNeeded, and emails creator.</summary>
    Task<WorkItemDto> RequestClarificationAsync(Guid workItemId, Guid reviewerId, string clarificationComment, CancellationToken ct = default);

    /// <summary>Denies a work item; validates comment >= 5 chars, transitions to Denied, emails creator, and permanently blocks external sync.</summary>
    Task<WorkItemDto> DenyReviewAsync(Guid workItemId, Guid reviewerId, string denialReason, CancellationToken ct = default);
}
