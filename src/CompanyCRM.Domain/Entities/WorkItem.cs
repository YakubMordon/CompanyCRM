using CompanyCRM.Domain.Common;
using CompanyCRM.Domain.Enums;

namespace CompanyCRM.Domain.Entities;

public class WorkItem : BaseEntity<Guid>
{
    public WorkItem()
    {
        Id = Guid.NewGuid();
    }

    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public string Code { get; set; } = string.Empty; // e.g., "CRM-101"
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }

    public WorkItemType Type { get; set; } = WorkItemType.Task;
    public WorkItemStatus Status { get; set; } = WorkItemStatus.Backlog;
    public WorkItemPriority Priority { get; set; } = WorkItemPriority.Medium;

    public decimal EstimatedHours { get; set; } = 0.0m;
    public int StoryPoints { get; set; } = 0;
    public string? SprintName { get; set; }
    public int BoardPosition { get; set; } = 0;

    // Assignments
    public Guid CreatedById { get; set; }
    public Employee CreatedBy { get; set; } = null!;

    public Guid? AssigneeId { get; set; }
    public Employee? Assignee { get; set; }

    // Reviewer Workflow
    public Guid? ReviewerId { get; set; }
    public Employee? Reviewer { get; set; }
    public ReviewStatus ReviewStatus { get; set; } = ReviewStatus.NotRequired;
    public string? ReviewLatestComment { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }

    // External Synchronization
    public ExternalSourceType ExternalSource { get; set; } = ExternalSourceType.None;
    public string? ExternalId { get; set; }
    public string? ExternalUrl { get; set; }
    public DateTimeOffset? LastSyncedAt { get; set; }
    public string? LastSyncedContentHash { get; set; }
    public SyncOrigin LastSyncOrigin { get; set; } = SyncOrigin.LocalCrm;

    // Navigation Collections
    public ICollection<WorkItemHistory> Histories { get; set; } = new List<WorkItemHistory>();
    public ICollection<TimeEntry> TimeEntries { get; set; } = new List<TimeEntry>();
    public ICollection<DailyPlanItem> DailyPlanItems { get; set; } = new List<DailyPlanItem>();
    public ICollection<SyncConflict> SyncConflicts { get; set; } = new List<SyncConflict>();

    // Domain Invariant Methods
    public bool CanTransitionKanban()
    {
        return Status != WorkItemStatus.PendingReview &&
               Status != WorkItemStatus.ClarificationRequested &&
               Status != WorkItemStatus.Denied &&
               ReviewStatus != ReviewStatus.Pending &&
               ReviewStatus != ReviewStatus.ClarificationNeeded &&
               ReviewStatus != ReviewStatus.Denied;
    }

    public bool CanPushToExternal()
    {
        if (ReviewStatus == ReviewStatus.Pending || 
            ReviewStatus == ReviewStatus.ClarificationNeeded || 
            ReviewStatus == ReviewStatus.Denied)
        {
            return false;
        }

        if (Status == WorkItemStatus.Draft ||
            Status == WorkItemStatus.PendingReview ||
            Status == WorkItemStatus.ClarificationRequested ||
            Status == WorkItemStatus.Denied)
        {
            return false;
        }

        return true;
    }

    public void AssignReviewer(Guid reviewerId)
    {
        ReviewerId = reviewerId;
        ReviewStatus = ReviewStatus.Pending;
        Status = WorkItemStatus.PendingReview;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void ApproveReview(Guid reviewerId, WorkItemStatus startingStatus = WorkItemStatus.Todo)
    {
        if (ReviewStatus == ReviewStatus.Denied)
            throw new InvalidOperationException("Cannot approve a denied work item.");

        ReviewerId = reviewerId;
        ReviewStatus = ReviewStatus.Approved;
        Status = startingStatus;
        ReviewedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void RequestClarification(Guid reviewerId, string comment)
    {
        if (string.IsNullOrWhiteSpace(comment) || comment.Trim().Length < 5)
            throw new ArgumentException("Clarification note must contain at least 5 characters.", nameof(comment));

        ReviewerId = reviewerId;
        ReviewStatus = ReviewStatus.ClarificationNeeded;
        Status = WorkItemStatus.ClarificationRequested;
        ReviewLatestComment = comment.Trim();
        ReviewedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void DenyReview(Guid reviewerId, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 5)
            throw new ArgumentException("Denial reason must contain at least 5 characters.", nameof(reason));

        ReviewerId = reviewerId;
        ReviewStatus = ReviewStatus.Denied;
        Status = WorkItemStatus.Denied;
        ReviewLatestComment = reason.Trim();
        ReviewedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
