namespace CompanyCRM.Application.DTOs;

using CompanyCRM.Domain.Enums;

public record CutoffExecutionSummaryDto(
    string Status, // "Completed", "Disabled", "BeforeCutoff", "ExcludedDay"
    DateOnly CheckedDate,
    TimeOnly CutoffTime,
    string TimeZoneId,
    int RemindersSent,
    IReadOnlyList<string> NotifiedEmployeeEmails,
    long ExecutionDurationMs,
    IReadOnlyList<string> Errors
);

public record WeeklyPlanDto(
    Guid Id,
    Guid EmployeeId,
    string EmployeeName,
    Guid? ProjectId,
    string? ProjectName,
    DateOnly WeekStartDate,
    string GoalsMarkdown,
    string? FocusAreasJson,
    string? KeyDeliverables,
    WeeklyPlanStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);

public record DailyPlanDto(
    Guid Id,
    Guid EmployeeId,
    string EmployeeName,
    DateOnly PlanDate,
    DateTimeOffset SubmittedAt,
    bool IsSubmittedOnTime,
    string? Blockers,
    string? SummaryNotes,
    IReadOnlyList<DailyPlanItemDto> Items
);

public record DailyPlanItemDto(
    Guid Id,
    Guid DailyPlanId,
    Guid? WorkItemId,
    string? WorkItemCode,
    string? WorkItemTitle,
    string? CustomTaskTitle,
    decimal PlannedHours,
    string? GoalNote,
    bool IsCompleted
);

public record PlanningCutoffConfigDto(
    Guid Id,
    Guid? ProjectId,
    string? ProjectName,
    TimeOnly CutoffTimeLocal,
    string TimeZoneId,
    bool IsReminderEnabled,
    string ExcludedDaysOfWeekJson,
    DateTimeOffset UpdatedAt
);

public record SubmitDailyPlanRequestDto(
    DateOnly PlanDate,
    string? Blockers,
    string? SummaryNotes,
    IReadOnlyList<SubmitDailyPlanItemDto> Items,
    Guid? ProjectId = null
);

public record SubmitDailyPlanItemDto(
    Guid? WorkItemId,
    string? CustomTaskTitle,
    decimal PlannedHours,
    string? GoalNote
);

public record CreateOrUpdateWeeklyPlanDto(
    DateOnly WeekStartDate,
    Guid? ProjectId,
    string GoalsMarkdown,
    string? FocusAreasJson,
    string? KeyDeliverables,
    WeeklyPlanStatus Status = WeeklyPlanStatus.Submitted
);

public record UpdatePlanningCutoffConfigDto(
    Guid? ProjectId,
    TimeOnly CutoffTimeLocal,
    string TimeZoneId,
    bool IsReminderEnabled,
    string ExcludedDaysOfWeekJson
);
