using CompanyCRM.Application.Interfaces;
using CompanyCRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CompanyCRM.Infrastructure.Persistence.Repositories;

public class PlanningRepository : IPlanningRepository
{
    private readonly CrmDbContext _dbContext;

    public PlanningRepository(CrmDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<List<WeeklyPlan>> GetWeeklyPlansAsync(DateOnly? normalizedMonday, Guid? projectId, Guid? employeeId, CancellationToken ct = default)
    {
        var query = _dbContext.WeeklyPlans
            .AsNoTracking()
            .Include(wp => wp.Employee)
            .Include(wp => wp.Project)
            .AsQueryable();

        if (normalizedMonday.HasValue)
        {
            query = query.Where(wp => wp.WeekStartDate == normalizedMonday.Value);
        }

        if (projectId.HasValue)
        {
            query = query.Where(wp => wp.ProjectId == projectId.Value);
        }

        if (employeeId.HasValue)
        {
            query = query.Where(wp => wp.EmployeeId == employeeId.Value);
        }

        return query.OrderByDescending(wp => wp.WeekStartDate).ToListAsync(ct);
    }

    public Task<WeeklyPlan?> GetWeeklyPlanByEmployeeAndWeekAsync(Guid employeeId, DateOnly normalizedMonday, bool trackChanges = false, CancellationToken ct = default)
    {
        var query = _dbContext.WeeklyPlans
            .Include(wp => wp.Employee)
            .Include(wp => wp.Project)
            .AsQueryable();

        if (!trackChanges)
        {
            query = query.AsNoTracking();
        }

        return query.FirstOrDefaultAsync(wp => wp.EmployeeId == employeeId && wp.WeekStartDate == normalizedMonday, ct);
    }

    public void AddWeeklyPlan(WeeklyPlan plan)
    {
        _dbContext.WeeklyPlans.Add(plan);
    }

    public Task<List<DailyPlan>> GetDailyPlansAsync(DateOnly? date, Guid? employeeId, CancellationToken ct = default)
    {
        var query = _dbContext.DailyPlans
            .AsNoTracking()
            .Include(dp => dp.Employee)
            .Include(dp => dp.Items)
                .ThenInclude(dpi => dpi.WorkItem)
            .AsQueryable();

        if (date.HasValue)
        {
            query = query.Where(dp => dp.PlanDate == date.Value);
        }

        if (employeeId.HasValue)
        {
            query = query.Where(dp => dp.EmployeeId == employeeId.Value);
        }

        return query.OrderByDescending(dp => dp.PlanDate).ToListAsync(ct);
    }

    public Task<DailyPlan?> GetDailyPlanByEmployeeAndDateAsync(Guid employeeId, DateOnly planDate, bool trackChanges = false, CancellationToken ct = default)
    {
        var query = _dbContext.DailyPlans
            .Include(dp => dp.Items)
            .AsQueryable();

        if (!trackChanges)
        {
            query = query.AsNoTracking();
        }

        return query.FirstOrDefaultAsync(dp => dp.EmployeeId == employeeId && dp.PlanDate == planDate, ct);
    }

    public Task<DailyPlan?> GetDailyPlanByIdWithDetailsAsync(Guid id, CancellationToken ct = default)
    {
        return _dbContext.DailyPlans
            .AsNoTracking()
            .Include(dp => dp.Items)
                .ThenInclude(dpi => dpi.WorkItem)
            .FirstOrDefaultAsync(dp => dp.Id == id, ct);
    }

    public void AddDailyPlan(DailyPlan plan)
    {
        _dbContext.DailyPlans.Add(plan);
    }

    public void ClearDailyPlanItems(DailyPlan plan)
    {
        if (plan.Items.Count > 0)
        {
            _dbContext.DailyPlanItems.RemoveRange(plan.Items.ToList());
            plan.Items.Clear();
        }
    }

    public void AddDailyPlanItem(DailyPlanItem item)
    {
        _dbContext.DailyPlanItems.Add(item);
    }

    public Task<Guid?> GetFirstConfiguredCutoffProjectIdAsync(IReadOnlyCollection<Guid> linkedProjectIds, CancellationToken ct = default)
    {
        return _dbContext.PlanningCutoffConfigs
            .AsNoTracking()
            .Where(c => c.ProjectId.HasValue && linkedProjectIds.Contains(c.ProjectId.Value))
            .OrderBy(c => c.ProjectId)
            .Select(c => c.ProjectId)
            .FirstOrDefaultAsync(ct);
    }

    public Task<PlanningCutoffConfig?> GetCutoffConfigAsync(Guid? projectId, bool trackChanges = false, CancellationToken ct = default)
    {
        var query = _dbContext.PlanningCutoffConfigs
            .Include(c => c.Project)
            .AsQueryable();

        if (!trackChanges)
        {
            query = query.AsNoTracking();
        }

        return query.FirstOrDefaultAsync(c => c.ProjectId == projectId, ct);
    }

    public void AddCutoffConfig(PlanningCutoffConfig config)
    {
        _dbContext.PlanningCutoffConfigs.Add(config);
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
    {
        return _dbContext.SaveChangesAsync(ct);
    }
}
