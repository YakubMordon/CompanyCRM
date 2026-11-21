namespace CompanyCRM.Application.DTOs;

using System.ComponentModel.DataAnnotations;
using CompanyCRM.Domain.Enums;

public record WorkItemDto(
    Guid Id,
    Guid ProjectId,
    string ProjectCode,
    string Code,
    string Title,
    string? Description,
    WorkItemType Type,
    WorkItemStatus Status,
    WorkItemPriority Priority,
    decimal EstimatedHours,
    int StoryPoints,
    string? SprintName,
    int BoardPosition,
    Guid CreatedById,
    string CreatedByName,
    Guid? AssigneeId,
    string? AssigneeName,
    string? AssigneeEmail,
    Guid? ReviewerId,
    string? ReviewerName,
    ReviewStatus ReviewStatus,
    string? ReviewLatestComment,
    DateTimeOffset? ReviewedAt,
    ExternalSourceType ExternalSource,
    string? ExternalId,
    string? ExternalUrl,
    DateTimeOffset? LastSyncedAt,
    string? LastSyncedContentHash,
    SyncOrigin LastSyncOrigin,
    int TotalLoggedMinutes,
    int BillableMinutes,
    bool IsTimerRunningForCurrentUser,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);

public record WorkItemHistoryDto(
    Guid Id,
    Guid WorkItemId,
    Guid ChangedById,
    string ChangedByName,
    DateTimeOffset ChangedAt,
    string Action,
    string? FieldName,
    string? OldValue,
    string? NewValue,
    string? Note
);

public record CreateWorkItemDto(
    [Required] Guid ProjectId,
    [Required, StringLength(200)] string Title,
    [StringLength(4000)] string? Description = null,
    WorkItemType Type = WorkItemType.Task,
    WorkItemPriority Priority = WorkItemPriority.Medium,
    Guid? AssigneeId = null,
    Guid? ReviewerId = null,
    [Range(0, 9999.99)] decimal EstimatedHours = 0.0m,
    [Range(0, 1000)] int StoryPoints = 0,
    [StringLength(100)] string? SprintName = null
);

public record UpdateWorkItemDto(
    [StringLength(200)] string? Title = null,
    [StringLength(4000)] string? Description = null,
    WorkItemType? Type = null,
    WorkItemStatus? Status = null,
    WorkItemPriority? Priority = null,
    Guid? AssigneeId = null,
    Guid? ReviewerId = null,
    [Range(0, 9999.99)] decimal? EstimatedHours = null,
    [Range(0, 1000)] int? StoryPoints = null,
    [StringLength(100)] string? SprintName = null,
    int? BoardPosition = null
);

public record TimeEntryDto(
    Guid Id,
    Guid WorkItemId,
    string WorkItemCode,
    string WorkItemTitle,
    Guid EmployeeId,
    string EmployeeName,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    int DurationMinutes,
    DateOnly LoggedDate,
    bool IsRunning,
    bool IsBillable,
    string? Notes
);

public record TimeSummaryDto(
    Guid WorkItemId,
    decimal TotalHours,
    decimal BillableHours,
    decimal NonBillableHours,
    decimal EstimatedHours
);

public record KanbanTransitionDto(
    WorkItemStatus Status,
    int? BoardPosition = null
);

public record StartTimerRequestDto(
    bool? IsBillable = true
);

public record StopTimerRequestDto(
    string? Notes = null,
    bool? IsBillable = null
);

public record CreateManualTimeEntryDto(
    [Range(1, 1440, ErrorMessage = "DurationMinutes must be between 1 and 1440 (24 hours).")] int DurationMinutes,
    bool IsBillable,
    [StringLength(1000)] string? Notes,
    DateOnly LoggedDate
);

public record PagedResult<T>(
    IReadOnlyList<T> Items,
    int TotalCount,
    int Page,
    int PageSize,
    int TotalPages
);
