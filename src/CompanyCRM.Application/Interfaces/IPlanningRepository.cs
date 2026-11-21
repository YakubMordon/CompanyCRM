using CompanyCRM.Domain.Entities;

namespace CompanyCRM.Application.Interfaces;

public interface IPlanningRepository
{
    Task<List<WeeklyPlan>> GetWeeklyPlansAsync(DateOnly? normalizedMonday, Guid? projectId, Guid? employeeId, CancellationToken ct = default);
    Task<WeeklyPlan?> GetWeeklyPlanByEmployeeAndWeekAsync(Guid employeeId, DateOnly normalizedMonday, bool trackChanges = false, CancellationToken ct = default);
    void AddWeeklyPlan(WeeklyPlan plan);

    Task<List<DailyPlan>> GetDailyPlansAsync(DateOnly? date, Guid? employeeId, CancellationToken ct = default);
    Task<DailyPlan?> GetDailyPlanByEmployeeAndDateAsync(Guid employeeId, DateOnly planDate, bool trackChanges = false, CancellationToken ct = default);
    Task<DailyPlan?> GetDailyPlanByIdWithDetailsAsync(Guid id, CancellationToken ct = default);
    void AddDailyPlan(DailyPlan plan);
    void ClearDailyPlanItems(DailyPlan plan);
    void AddDailyPlanItem(DailyPlanItem item);

    Task<Guid?> GetFirstConfiguredCutoffProjectIdAsync(IReadOnlyCollection<Guid> linkedProjectIds, CancellationToken ct = default);
    Task<PlanningCutoffConfig?> GetCutoffConfigAsync(Guid? projectId, bool trackChanges = false, CancellationToken ct = default);
    void AddCutoffConfig(PlanningCutoffConfig config);

    Task SaveChangesAsync(CancellationToken ct = default);
}
