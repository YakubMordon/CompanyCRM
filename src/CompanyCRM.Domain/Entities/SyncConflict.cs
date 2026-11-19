using CompanyCRM.Domain.Enums;

namespace CompanyCRM.Domain.Entities;

public class SyncConflict
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public Guid? LocalWorkItemId { get; set; }
    public WorkItem? LocalWorkItem { get; set; }

    public ExternalSourceType ExternalSource { get; set; } = ExternalSourceType.AzureDevOps;
    public string ExternalId { get; set; } = string.Empty;
    public string ExternalTitle { get; set; } = string.Empty;
    public string? ExternalDescription { get; set; }
    public string ExternalStatus { get; set; } = string.Empty;
    public string? ExternalAssigneeEmail { get; set; }

    public double SimilarityScore { get; set; } = 0.0;
    public SyncConflictType ConflictType { get; set; } = SyncConflictType.DuplicateCandidate;
    public ConflictStatus Status { get; set; } = ConflictStatus.Pending;
    public ConflictResolutionAction ResolutionAction { get; set; } = ConflictResolutionAction.Unresolved;
    public string? ResolutionNotes { get; set; }

    public DateTimeOffset DetectedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ResolvedAt { get; set; }

    public Guid? ResolvedById { get; set; }
    public Employee? ResolvedBy { get; set; }
}
