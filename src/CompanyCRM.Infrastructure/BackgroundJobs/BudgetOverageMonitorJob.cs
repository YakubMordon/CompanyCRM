using CompanyCRM.Application.Interfaces;

namespace CompanyCRM.Infrastructure.BackgroundJobs;

public class BudgetOverageMonitorJob(IRetainerService retainerService)
{
    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        await retainerService.EvaluateBudgetOveragesAsync(ct: ct);
    }
}
