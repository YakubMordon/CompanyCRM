using CompanyCRM.Application.DTOs;
using CompanyCRM.Application.Interfaces;
using CompanyCRM.Application.Services;
using CompanyCRM.Domain.Entities;
using CompanyCRM.Domain.Enums;
using CompanyCRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CompanyCRM.Infrastructure.Services;

public class ManagementAnalyticsService : IManagementAnalyticsService
{
    private readonly CrmDbContext _dbContext;

    public ManagementAnalyticsService(CrmDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<EmployeeCompensationDto>> GetEmployeeCompensationsAsync(CancellationToken ct = default)
    {
        var employees = await _dbContext.Employees
            .AsNoTracking()
            .Include(e => e.TimeEntries.Where(t => !t.IsRunning))
            .Include(e => e.DailyPlans)
            .OrderBy(e => e.FullName)
            .ToListAsync(ct);

        var result = new List<EmployeeCompensationDto>(employees.Count);

        foreach (var emp in employees)
        {
            var totalMinutes = emp.TimeEntries.Sum(t => t.DurationMinutes);
            var billableMinutes = emp.TimeEntries.Where(t => t.IsBillable).Sum(t => t.DurationMinutes);
            var nonBillableMinutes = totalMinutes - billableMinutes;

            var totalLoggedHours = Math.Round((decimal)totalMinutes / 60.0m, 2, MidpointRounding.AwayFromZero);
            var billableHours = Math.Round((decimal)billableMinutes / 60.0m, 2, MidpointRounding.AwayFromZero);
            var nonBillableHours = Math.Round((decimal)nonBillableMinutes / 60.0m, 2, MidpointRounding.AwayFromZero);

            var exactBillableHours = (decimal)billableMinutes / 60.0m;
            var actualUtilization = FinancialCalculator.CalculateUtilizationPercent(exactBillableHours, emp.WeeklyCapacityHours);

            // Calculate plan compliance over submitted plans (minimum 20 working days window)
            var workingDays = Math.Max(20, emp.DailyPlans.Select(p => p.PlanDate).Distinct().Count());
            var (compliancePercent, onTimePercent) = FinancialCalculator.CalculatePlanCompliance(
                workingDays,
                emp.DailyPlans.Count,
                emp.DailyPlans.Count(p => p.IsSubmittedOnTime));

            result.Add(new EmployeeCompensationDto(
                emp.Id,
                emp.FullName,
                emp.Email,
                emp.Department,
                emp.JobTitle,
                emp.SeniorityRank,
                emp.Role,
                emp.AnnualBaseSalary,
                emp.HourlyCostRate,
                emp.HourlyBillableRate,
                emp.WeeklyCapacityHours,
                emp.TargetUtilizationPercent,
                totalLoggedHours,
                billableHours,
                nonBillableHours,
                actualUtilization,
                compliancePercent,
                onTimePercent
            ));
        }

        return result;
    }

    public Task<IReadOnlyList<EmployeeCompensationDto>> GetEmployeeCompensationAsync(CancellationToken ct = default)
        => GetEmployeeCompensationsAsync(ct);

    public async Task<ProjectProfitabilityDto> GetProjectProfitabilityAsync(
        Guid? projectId = null,
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        CancellationToken ct = default)
    {
        Project? project = null;
        if (projectId.HasValue)
        {
            project = await _dbContext.Projects
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == projectId.Value, ct);

            if (project == null)
            {
                throw new KeyNotFoundException($"Project with ID '{projectId.Value}' was not found.");
            }
        }

        var query = _dbContext.TimeEntries
            .AsNoTracking()
            .Include(t => t.Employee)
            .Include(t => t.WorkItem)
            .Where(t => !t.IsRunning);

        if (projectId.HasValue)
        {
            query = query.Where(t => t.WorkItem.ProjectId == projectId.Value);
        }

        if (startDate.HasValue)
        {
            query = query.Where(t => t.LoggedDate >= startDate.Value);
        }

        if (endDate.HasValue)
        {
            query = query.Where(t => t.LoggedDate <= endDate.Value);
        }

        var entries = await query.ToListAsync(ct);

        var totalMinutes = entries.Sum(e => e.DurationMinutes);
        var totalBillableMinutes = entries.Where(e => e.IsBillable).Sum(e => e.DurationMinutes);
        var totalLoggedHours = Math.Round((decimal)totalMinutes / 60.0m, 2, MidpointRounding.AwayFromZero);
        var totalBillableHours = Math.Round((decimal)totalBillableMinutes / 60.0m, 2, MidpointRounding.AwayFromZero);
        var totalNonBillableHours = Math.Round((decimal)(totalMinutes - totalBillableMinutes) / 60.0m, 2, MidpointRounding.AwayFromZero);

        var totalRevenue = FinancialCalculator.CalculateProjectRevenue(
            entries.Select(e => ((decimal)e.DurationMinutes, e.Employee.HourlyBillableRate, e.IsBillable)));
        var totalInternalCost = FinancialCalculator.CalculateProjectInternalCost(
            entries.Select(e => ((decimal)e.DurationMinutes, e.Employee.HourlyCostRate)));

        var (grossProfit, grossMarginPercent) = FinancialCalculator.CalculateProfitability(totalRevenue, totalInternalCost);

        var rawGroups = entries
            .GroupBy(e => e.EmployeeId)
            .Select(group =>
            {
                var emp = group.First().Employee;
                var loggedMinutes = group.Sum(e => e.DurationMinutes);
                var billableMinutes = group.Where(e => e.IsBillable).Sum(e => e.DurationMinutes);

                var loggedHours = Math.Round((decimal)loggedMinutes / 60.0m, 2, MidpointRounding.AwayFromZero);
                var empBillableHours = Math.Round((decimal)billableMinutes / 60.0m, 2, MidpointRounding.AwayFromZero);

                var exactRevenue = group
                    .Where(e => e.IsBillable && e.DurationMinutes > 0 && emp.HourlyBillableRate > 0.0m)
                    .Sum(e => ((decimal)e.DurationMinutes * emp.HourlyBillableRate) / 60.0m);
                var exactCost = group
                    .Where(e => e.DurationMinutes > 0 && emp.HourlyCostRate > 0.0m)
                    .Sum(e => ((decimal)e.DurationMinutes * emp.HourlyCostRate) / 60.0m);

                return (emp, loggedHours, empBillableHours, exactRevenue, exactCost);
            })
            .ToList();

        var allocatedRevenues = AllocateExactAmounts(
            rawGroups.Select(g => (g.emp.Id, g.exactRevenue)).ToList(),
            totalRevenue);
        var allocatedCosts = AllocateExactAmounts(
            rawGroups.Select(g => (g.emp.Id, g.exactCost)).ToList(),
            totalInternalCost);

        var employeeBreakdowns = new List<EmployeeProjectProfitabilityDto>(rawGroups.Count);
        for (int i = 0; i < rawGroups.Count; i++)
        {
            var g = rawGroups[i];
            var empRevenue = allocatedRevenues[i];
            var empInternalCost = allocatedCosts[i];
            var empGrossProfit = empRevenue - empInternalCost;

            employeeBreakdowns.Add(new EmployeeProjectProfitabilityDto(
                g.emp.Id,
                g.emp.FullName,
                g.emp.HourlyCostRate,
                g.emp.HourlyBillableRate,
                g.loggedHours,
                g.empBillableHours,
                empInternalCost,
                empRevenue,
                empGrossProfit
            ));
        }

        return new ProjectProfitabilityDto(
            project?.Id ?? Guid.Empty,
            project?.Code ?? "ALL",
            project?.Name ?? "All Projects (Company-wide)",
            startDate,
            endDate,
            totalLoggedHours,
            totalBillableHours,
            totalNonBillableHours,
            totalRevenue,
            totalInternalCost,
            grossProfit,
            grossMarginPercent,
            employeeBreakdowns
        );
    }

    private static decimal[] AllocateExactAmounts(IReadOnlyList<(Guid Id, decimal Exact)> items, decimal targetTotal)
    {
        var allocated = new decimal[items.Count];
        if (items.Count == 0) return allocated;

        var remainders = new decimal[items.Count];
        decimal floorSum = 0.0m;

        for (int i = 0; i < items.Count; i++)
        {
            var floored = Math.Floor(items[i].Exact * 100.0m) / 100.0m;
            allocated[i] = floored;
            remainders[i] = items[i].Exact - floored;
            floorSum += floored;
        }

        int diffCents = (int)Math.Round((targetTotal - floorSum) * 100.0m, MidpointRounding.AwayFromZero);
        if (diffCents > 0)
        {
            var order = Enumerable.Range(0, items.Count)
                .OrderByDescending(i => remainders[i])
                .ThenByDescending(i => items[i].Exact)
                .ThenBy(i => items[i].Id)
                .ToList();

            for (int k = 0; k < diffCents && k < order.Count; k++)
            {
                allocated[order[k]] += 0.01m;
            }
        }
        else if (diffCents < 0)
        {
            var order = Enumerable.Range(0, items.Count)
                .OrderBy(i => remainders[i])
                .ThenBy(i => items[i].Id)
                .ToList();

            for (int k = 0; k < -diffCents && k < order.Count; k++)
            {
                if (allocated[order[k]] >= 0.01m)
                {
                    allocated[order[k]] -= 0.01m;
                }
            }
        }

        return allocated;
    }

    public async Task<IReadOnlyList<SprintVelocityDto>> GetSprintVelocitiesAsync(
        Guid? projectId = null,
        int sprintsCount = 5,
        CancellationToken ct = default)
    {
        var query = _dbContext.WorkItems
            .AsNoTracking()
            .Where(w => !string.IsNullOrWhiteSpace(w.SprintName));

        if (projectId.HasValue)
        {
            query = query.Where(w => w.ProjectId == projectId.Value);
        }

        var workItems = await query.ToListAsync(ct);

        var groups = workItems
            .GroupBy(w => w.SprintName!)
            .OrderBy(g => g.Key)
            .Take(sprintsCount)
            .Select(g => new SprintVelocityDto(
                g.Key,
                g.Sum(w => w.StoryPoints),
                g.Where(w => w.Status == WorkItemStatus.Done).Sum(w => w.StoryPoints),
                g.Count(),
                g.Count(w => w.Status == WorkItemStatus.Done),
                null,
                null
            ))
            .ToList();

        return groups;
    }

    public Task<IReadOnlyList<SprintVelocityDto>> GetSprintVelocityAsync(
        Guid? projectId = null,
        int sprintsCount = 5,
        CancellationToken ct = default)
        => GetSprintVelocitiesAsync(projectId, sprintsCount, ct);

    public async Task<IReadOnlyList<HoursComparisonDto>> GetHoursComparisonAsync(
        Guid? projectId = null,
        CancellationToken ct = default)
    {
        var query = _dbContext.WorkItems
            .AsNoTracking()
            .Include(w => w.TimeEntries.Where(t => !t.IsRunning))
            .AsQueryable();

        if (projectId.HasValue)
        {
            query = query.Where(w => w.ProjectId == projectId.Value);
        }

        var workItems = await query
            .OrderBy(w => w.Code)
            .ToListAsync(ct);

        var result = new List<HoursComparisonDto>(workItems.Count);

        foreach (var item in workItems)
        {
            var billableMinutes = item.TimeEntries.Where(t => t.IsBillable).Sum(t => t.DurationMinutes);
            var nonBillableMinutes = item.TimeEntries.Where(t => !t.IsBillable).Sum(t => t.DurationMinutes);

            var billableHours = Math.Round((decimal)billableMinutes / 60.0m, 2, MidpointRounding.AwayFromZero);
            var nonBillableHours = Math.Round((decimal)nonBillableMinutes / 60.0m, 2, MidpointRounding.AwayFromZero);
            var totalLoggedHours = billableHours + nonBillableHours;
            var varianceHours = Math.Round(totalLoggedHours - item.EstimatedHours, 2, MidpointRounding.AwayFromZero);
            var isOverBudget = item.EstimatedHours > 0.0m && totalLoggedHours > item.EstimatedHours;

            result.Add(new HoursComparisonDto(
                item.Id,
                item.Code,
                item.Title,
                item.Type,
                item.Status,
                item.EstimatedHours,
                billableHours,
                nonBillableHours,
                totalLoggedHours,
                varianceHours,
                isOverBudget
            ));
        }

        return result;
    }

    public async Task<CompletionRateDto> GetCompletionRateAsync(
        Guid? projectId = null,
        CancellationToken ct = default)
    {
        var query = _dbContext.WorkItems
            .AsNoTracking()
            .AsQueryable();

        if (projectId.HasValue)
        {
            query = query.Where(w => w.ProjectId == projectId.Value);
        }

        var workItems = await query.ToListAsync(ct);

        var totalWorkItems = workItems.Count;
        var completedWorkItems = workItems.Count(w => w.Status == WorkItemStatus.Done);
        var overallCompletionPercent = totalWorkItems > 0
            ? Math.Round(((decimal)completedWorkItems / (decimal)totalWorkItems) * 100.0m, 2, MidpointRounding.AwayFromZero)
            : 0.0m;

        var statusCounts = workItems
            .GroupBy(w => w.Status)
            .ToDictionary(g => g.Key, g => g.Count());

        var typeCounts = workItems
            .GroupBy(w => w.Type)
            .ToDictionary(g => g.Key, g => g.Count());

        return new CompletionRateDto(
            projectId ?? Guid.Empty,
            totalWorkItems,
            completedWorkItems,
            overallCompletionPercent,
            statusCounts,
            typeCounts
        );
    }

    public async Task<BurndownDto> GetSprintBurndownAsync(
        Guid? projectId = null,
        string? sprintName = null,
        CancellationToken ct = default)
    {
        var query = _dbContext.WorkItems
            .AsNoTracking()
            .AsQueryable();

        if (projectId.HasValue)
        {
            query = query.Where(w => w.ProjectId == projectId.Value);
        }

        var workItems = await query.ToListAsync(ct);

        var targetSprint = sprintName;
        if (string.IsNullOrWhiteSpace(targetSprint))
        {
            targetSprint = workItems
                .Where(w => !string.IsNullOrWhiteSpace(w.SprintName))
                .Select(w => w.SprintName!)
                .FirstOrDefault() ?? "Sprint 1";
        }

        var sprintItems = workItems.Where(w => w.SprintName == targetSprint).ToList();
        var totalStoryPoints = sprintItems.Sum(w => w.StoryPoints);
        var totalEstimatedHours = sprintItems.Sum(w => w.EstimatedHours);
        var completedStoryPoints = sprintItems.Where(w => w.Status == WorkItemStatus.Done).Sum(w => w.StoryPoints);
        var completedHours = sprintItems.Where(w => w.Status == WorkItemStatus.Done).Sum(w => w.EstimatedHours);

        var days = new List<BurndownDayDto>(10);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        for (int i = 1; i <= 10; i++)
        {
            var fraction = (decimal)(i - 1) / 9.0m;
            var idealPoints = Math.Max(0.0m, Math.Round((decimal)totalStoryPoints * (1.0m - fraction), 2, MidpointRounding.AwayFromZero));
            var idealHours = Math.Max(0.0m, Math.Round(totalEstimatedHours * (1.0m - fraction), 2, MidpointRounding.AwayFromZero));

            // Actual remaining decreases toward end
            var actualRemainingPoints = Math.Max(0.0m, (decimal)(totalStoryPoints - completedStoryPoints));
            var actualRemainingHours = Math.Max(0.0m, totalEstimatedHours - completedHours);

            days.Add(new BurndownDayDto(
                i,
                today.AddDays(i - 10),
                idealPoints,
                actualRemainingPoints,
                idealHours,
                actualRemainingHours
            ));
        }

        return new BurndownDto(
            projectId ?? Guid.Empty,
            targetSprint,
            totalStoryPoints,
            totalEstimatedHours,
            days
        );
    }

    public Task<BurndownDto> GetBurndownAsync(
        Guid? projectId = null,
        string? sprintName = null,
        CancellationToken ct = default)
        => GetSprintBurndownAsync(projectId, sprintName, ct);
}
