namespace CompanyCRM.Application.DTOs;

using CompanyCRM.Domain.Enums;

public record ExternalWorkItemDto(
    string ExternalId,
    string ExternalUrl,
    string Title,
    string? Description,
    string Status,
    string Type,
    string? AssigneeEmail,
    DateTimeOffset? LastModifiedAt = null,
    string? ContentHash = null
);

public record SyncResultDto(
    bool Success,
    Guid WorkItemId,
    string? ExternalId,
    string? ExternalUrl,
    string? ContentHash,
    string? ErrorMessage,
    SyncOrigin Origin
);

public record WebhookProcessingResultDto(
    string Status, // "IgnoredEcho", "Applied", "ConflictDetected", "Skipped", "Error"
    string? ExternalId,
    Guid? WorkItemId,
    bool IsEcho,
    string Message,
    SyncConflictDto? Conflict = null
);

public record ReconciliationResultDto(
    Guid? ProjectId,
    int TotalItemsChecked,
    int UpdatedCount,
    int ConflictsCount,
    int SkippedCount,
    IReadOnlyList<string> Errors,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt
);

public record SyncConflictDto(
    Guid Id,
    Guid ProjectId,
    Guid? LocalWorkItemId,
    string? LocalWorkItemCode,
    string? LocalTitle,
    ExternalSourceType ExternalSource,
    string ExternalId,
    string ExternalTitle,
    string? ExternalDescription,
    string ExternalStatus,
    string? ExternalAssigneeEmail,
    double SimilarityScore,
    SyncConflictType ConflictType,
    ConflictStatus Status,
    ConflictResolutionAction ResolutionAction,
    string? ResolutionNotes,
    DateTimeOffset DetectedAt,
    DateTimeOffset? ResolvedAt,
    string? LocalStatus = null,
    string? LocalAssigneeName = null
);

public record ConflictResolutionResultDto(
    Guid ConflictId,
    ConflictResolutionAction Action,
    bool Resolved,
    Guid? ResolvedWorkItemId,
    string? Message
);

public record ResolveConflictRequestDto(
    ConflictResolutionAction Action,
    string? ResolutionNotes = null
);
