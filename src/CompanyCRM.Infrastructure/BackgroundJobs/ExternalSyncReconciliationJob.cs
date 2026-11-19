using CompanyCRM.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CompanyCRM.Infrastructure.BackgroundJobs;

public class ExternalSyncReconciliationJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ExternalSyncReconciliationJob> _logger;

    public ExternalSyncReconciliationJob(
        IServiceScopeFactory scopeFactory,
        ILogger<ExternalSyncReconciliationJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task RunReconciliationAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Starting scheduled external sync reconciliation background job...");
        using var scope = _scopeFactory.CreateScope();
        var syncOrchestrator = scope.ServiceProvider.GetRequiredService<ISyncOrchestratorService>();

        try
        {
            var result = await syncOrchestrator.RunReconciliationAsync(null, ct);
            _logger.LogInformation(
                "External sync reconciliation completed. Total checked: {Total}, Updated: {Updated}, Conflicts: {Conflicts}, Skipped: {Skipped}, Errors: {ErrorCount}",
                result.TotalItemsChecked,
                result.UpdatedCount,
                result.ConflictsCount,
                result.SkippedCount,
                result.Errors.Count);

            if (result.Errors.Count > 0)
            {
                foreach (var err in result.Errors)
                {
                    _logger.LogWarning("Reconciliation project error: {Error}", err);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in external sync reconciliation job");
        }
    }
}
