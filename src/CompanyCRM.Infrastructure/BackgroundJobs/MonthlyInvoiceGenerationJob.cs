using CompanyCRM.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CompanyCRM.Infrastructure.BackgroundJobs;

public class MonthlyInvoiceGenerationJob(
    IServiceScopeFactory scopeFactory,
    ILogger<MonthlyInvoiceGenerationJob> logger)
{
    public async Task ExecutePreviousMonthAsync(CancellationToken ct = default)
    {
        var target = DateTime.UtcNow.AddMonths(-1);
        using var scope = scopeFactory.CreateScope();
        var invoiceService = scope.ServiceProvider.GetRequiredService<IInvoiceService>();

        var generated = await invoiceService.GenerateMonthlyInvoicesAsync(target.Year, target.Month, ct: ct);
        logger.LogInformation(
            "MonthlyInvoiceGenerationJob generated {Count} draft invoices for {Year}-{Month:D2}.",
            generated.Count, target.Year, target.Month);
    }
}
