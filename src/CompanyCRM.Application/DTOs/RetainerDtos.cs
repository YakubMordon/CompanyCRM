namespace CompanyCRM.Application.DTOs;

public enum RetainerHealthStatus
{
    Healthy = 0,
    Warning = 1,
    Exceeded = 2
}

public record RetainerStatusDto(
    Guid Id,
    Guid ProjectId,
    string ProjectName,
    string ProjectCode,
    string ClientName,
    decimal MonthlyRetainerHours,
    decimal MonthlyBudgetAmount,
    string Currency,
    decimal WarningThresholdPercent,
    bool IsActive,
    bool NotifySlackTeams,
    bool NotifyEmail,
    int Year,
    int Month,
    decimal LoggedBillableHours,
    decimal LoggedBillableAmount,
    decimal RemainingHours,
    decimal RemainingBudget,
    decimal HoursUtilizationPercent,
    decimal BudgetUtilizationPercent,
    RetainerHealthStatus HealthStatus,
    DateTimeOffset UpdatedAt
);

public record UpsertRetainerRequestDto(
    Guid ProjectId,
    string ClientName,
    decimal MonthlyRetainerHours,
    decimal MonthlyBudgetAmount,
    string Currency = "USD",
    decimal WarningThresholdPercent = 80m,
    bool IsActive = true,
    bool NotifySlackTeams = true,
    bool NotifyEmail = true
);

public record BudgetOverageEvaluationResultDto(
    int EvaluatedRetainers,
    int AlertsTriggered,
    IReadOnlyList<string> TriggeredMessages
);
