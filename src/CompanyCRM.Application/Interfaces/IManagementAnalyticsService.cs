namespace CompanyCRM.Application.Interfaces;

using CompanyCRM.Application.DTOs;

public interface IManagementAnalyticsService
{
    /// <summary>Retrieves sensitive employee compensation, cost/billable rates, and actual utilization metrics.</summary>
    Task<IReadOnlyList<EmployeeCompensationDto>> GetEmployeeCompensationsAsync(CancellationToken ct = default);

    /// <summary>Alias for GetEmployeeCompensationsAsync.</summary>
    Task<IReadOnlyList<EmployeeCompensationDto>> GetEmployeeCompensationAsync(CancellationToken ct = default)
        => GetEmployeeCompensationsAsync(ct);

    /// <summary>Calculates client billable revenue, internal labor cost, gross profit, and gross margin % for a project.</summary>
    Task<ProjectProfitabilityDto> GetProjectProfitabilityAsync(Guid projectId, DateOnly? startDate = null, DateOnly? endDate = null, CancellationToken ct = default);

    /// <summary>Calculates planned vs. completed story points and item counts across recent sprints.</summary>
    Task<IReadOnlyList<SprintVelocityDto>> GetSprintVelocitiesAsync(Guid projectId, int sprintsCount = 5, CancellationToken ct = default);

    /// <summary>Alias for GetSprintVelocitiesAsync.</summary>
    Task<IReadOnlyList<SprintVelocityDto>> GetSprintVelocityAsync(Guid projectId, int sprintsCount = 5, CancellationToken ct = default)
        => GetSprintVelocitiesAsync(projectId, sprintsCount, ct);

    /// <summary>Calculates estimated vs. logged billable/non-billable hours and budget variance per work item.</summary>
    Task<IReadOnlyList<HoursComparisonDto>> GetHoursComparisonAsync(Guid projectId, CancellationToken ct = default);

    /// <summary>Calculates completion rate breakdown and status distributions for a project.</summary>
    Task<CompletionRateDto> GetCompletionRateAsync(Guid projectId, CancellationToken ct = default);

    /// <summary>Calculates sprint burndown metrics (ideal vs. actual remaining story points and hours) per day.</summary>
    Task<BurndownDto> GetSprintBurndownAsync(Guid projectId, string? sprintName = null, CancellationToken ct = default);

    /// <summary>Alias for GetSprintBurndownAsync.</summary>
    Task<BurndownDto> GetBurndownAsync(Guid projectId, string? sprintName = null, CancellationToken ct = default)
        => GetSprintBurndownAsync(projectId, sprintName, ct);
}
