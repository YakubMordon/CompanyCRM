namespace CompanyCRM.Application.DTOs;

using CompanyCRM.Domain.Enums;

public record EmployeeCompensationDto(
    Guid EmployeeId,
    string FullName,
    string Email,
    string Department,
    string JobTitle,
    SeniorityRank SeniorityRank,
    AppRole Role,
    decimal AnnualBaseSalary,
    decimal HourlyCostRate,
    decimal HourlyBillableRate,
    decimal WeeklyCapacityHours,
    decimal TargetUtilizationPercent,
    decimal TotalLoggedHours,
    decimal BillableHours,
    decimal NonBillableHours,
    decimal ActualUtilizationPercent,
    decimal DailyPlanCompliancePercent,
    decimal OnTimeSubmissionPercent
);

public record ProjectProfitabilityDto(
    Guid ProjectId,
    string ProjectCode,
    string ProjectName,
    DateOnly? StartDate,
    DateOnly? EndDate,
    decimal TotalLoggedHours,
    decimal TotalBillableHours,
    decimal TotalNonBillableHours,
    decimal TotalRevenue,
    decimal TotalInternalCost,
    decimal GrossProfit,
    decimal GrossMarginPercent,
    IReadOnlyList<EmployeeProjectProfitabilityDto> EmployeeBreakdowns
);

public record EmployeeProjectProfitabilityDto(
    Guid EmployeeId,
    string FullName,
    decimal HourlyCostRate,
    decimal HourlyBillableRate,
    decimal LoggedHours,
    decimal BillableHours,
    decimal InternalCost,
    decimal Revenue,
    decimal GrossProfit
);

public record SprintVelocityDto(
    string SprintName,
    int PlannedPoints,
    int CompletedPoints,
    int PlannedWorkItemsCount,
    int CompletedWorkItemsCount,
    DateOnly? StartDate,
    DateOnly? EndDate
);

public record HoursComparisonDto(
    Guid WorkItemId,
    string WorkItemCode,
    string Title,
    WorkItemType Type,
    WorkItemStatus Status,
    decimal EstimatedHours,
    decimal BillableHours,
    decimal NonBillableHours,
    decimal TotalLoggedHours,
    decimal VarianceHours,
    bool IsOverBudget
);

public record CompletionRateDto(
    Guid ProjectId,
    int TotalWorkItems,
    int CompletedWorkItems,
    decimal OverallCompletionPercent,
    IReadOnlyDictionary<WorkItemStatus, int> StatusCounts,
    IReadOnlyDictionary<WorkItemType, int> TypeCounts
);

public record BurndownDto(
    Guid ProjectId,
    string SprintName,
    int TotalStoryPoints,
    decimal TotalEstimatedHours,
    IReadOnlyList<BurndownDayDto> Days
);

public record BurndownDayDto(
    int DayIndex,
    DateOnly Date,
    decimal IdealRemainingPoints,
    decimal ActualRemainingPoints,
    decimal IdealRemainingHours,
    decimal ActualRemainingHours
);

public record EmployeeDirectoryDto(
    Guid Id,
    string FullName,
    string Email,
    string? AvatarUrl,
    string Department,
    string JobTitle,
    SeniorityRank SeniorityRank,
    AppRole Role,
    string? Bio,
    IReadOnlyList<EmployeeSkillDto> Skills
);

public record EmployeeProfileDto(
    Guid Id,
    string FullName,
    string Email,
    string? AvatarUrl,
    string Department,
    string JobTitle,
    SeniorityRank SeniorityRank,
    AppRole Role,
    string? Bio,
    string? LayoutPreferencesJson,
    IReadOnlyList<EmployeeSkillDto> Skills
);

public record EmployeeSkillDto(
    Guid Id,
    string SkillName,
    string Category,
    int ProficiencyLevel,
    decimal YearsOfExperience,
    DateTimeOffset? LastEvaluatedAt
);

public record UpdateProfileRequestDto(
    string? Bio,
    string? LayoutPreferencesJson
);

public record UpsertSkillRequestDto(
    string SkillName,
    string Category,
    int ProficiencyLevel,
    decimal YearsOfExperience = 1.0m
);
