namespace CompanyCRM.Application.DTOs;

using CompanyCRM.Domain.Enums;

public record ReviewQueueEventDto(
    Guid WorkItemId,
    string WorkItemCode,
    string WorkItemTitle,
    Guid ProjectId,
    Guid? ReviewerId,
    string Action, // "Assigned", "Approved", "ClarificationRequested", "Denied"
    ReviewStatus ReviewStatus,
    string? Comment,
    DateTimeOffset Timestamp
);

public record SyncEventDto(
    Guid ProjectId,
    Guid? WorkItemId,
    string Status, // "SyncStarted", "SyncCompleted", "WorkItemPushed", "ConflictDetected", "SyncFailed"
    string Message,
    string? ExternalId,
    DateTimeOffset Timestamp
)
{
    public string EventType => Status;
};

public record DailyPlanSubmittedDto(
    Guid EmployeeId,
    string EmployeeName,
    DateOnly PlanDate,
    bool IsOnTime,
    DateTimeOffset SubmittedAt,
    int ItemsCount
);
