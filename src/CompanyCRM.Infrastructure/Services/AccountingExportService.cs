using System.Globalization;
using System.Text;
using CompanyCRM.Application.DTOs;
using CompanyCRM.Application.Interfaces;
using CompanyCRM.Domain.Entities;
using CompanyCRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CompanyCRM.Infrastructure.Services;

public class AccountingExportService(CrmDbContext dbContext) : IAccountingExportService
{
    public async Task<(byte[] FileBytes, string ContentType, string FileName)> ExportInvoicesAsync(
        AccountingExportRequestDto request,
        CancellationToken ct = default)
    {
        var query = dbContext.Invoices
            .Include(i => i.Project)
            .Include(i => i.LineItems)
            .AsQueryable();

        if (request.ProjectId.HasValue) query = query.Where(i => i.ProjectId == request.ProjectId.Value);
        if (request.Year.HasValue) query = query.Where(i => i.Year == request.Year.Value);
        if (request.Month.HasValue) query = query.Where(i => i.Month == request.Month.Value);

        var invoices = await query
            .OrderByDescending(i => i.Year)
            .ThenByDescending(i => i.Month)
            .ThenBy(i => i.InvoiceNumber)
            .ToListAsync(ct);

        var providerName = request.Format switch
        {
            AccountingExportFormat.XeroCsv => "Xero",
            AccountingExportFormat.QuickBooksCsv => "QuickBooks CSV",
            AccountingExportFormat.QuickBooksIif => "QuickBooks IIF",
            _ => "Unknown"
        };

        var now = DateTimeOffset.UtcNow;
        foreach (var inv in invoices)
        {
            inv.LastExportedProvider = providerName;
            inv.LastExportedAt = now;
        }

        if (invoices.Count > 0)
        {
            await dbContext.SaveChangesAsync(ct);
        }

        return request.Format switch
        {
            AccountingExportFormat.XeroCsv => (
                Encoding.UTF8.GetBytes(BuildXeroCsv(invoices)),
                "text/csv",
                $"companycrm-xero-invoices-{now:yyyyMMdd}.csv"),

            AccountingExportFormat.QuickBooksCsv => (
                Encoding.UTF8.GetBytes(BuildQuickBooksCsv(invoices)),
                "text/csv",
                $"companycrm-quickbooks-invoices-{now:yyyyMMdd}.csv"),

            AccountingExportFormat.QuickBooksIif => (
                Encoding.UTF8.GetBytes(BuildQuickBooksIif(invoices)),
                "text/plain",
                $"companycrm-quickbooks-invoices-{now:yyyyMMdd}.iif"),

            _ => throw new ArgumentOutOfRangeException($"Unsupported accounting export format: {request.Format}")
        };
    }

    private static string BuildXeroCsv(IReadOnlyList<Invoice> invoices)
    {
        var sb = new StringBuilder();
        sb.AppendLine("*ContactName,*InvoiceNumber,*InvoiceDate,*DueDate,*Description,*Quantity,*UnitAmount,*AccountCode,*TaxType,Currency");

        foreach (var inv in invoices)
        {
            var invDate = inv.GeneratedAt.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var dueDate = inv.DueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var taxType = inv.TaxRatePercent > 0 ? "TAX001" : "NONE";

            foreach (var li in inv.LineItems)
            {
                sb.AppendLine(string.Join(",",
                    CsvEscape(inv.Project.Name),
                    CsvEscape(inv.InvoiceNumber),
                    invDate,
                    dueDate,
                    CsvEscape($"{li.EmployeeName} - {li.Description}"),
                    li.Hours.ToString("0.00", CultureInfo.InvariantCulture),
                    li.HourlyRate.ToString("0.00", CultureInfo.InvariantCulture),
                    "200",
                    taxType,
                    CsvEscape(inv.Currency)));
            }
        }

        return sb.ToString();
    }

    private static string BuildQuickBooksCsv(IReadOnlyList<Invoice> invoices)
    {
        var sb = new StringBuilder();
        sb.AppendLine("InvoiceNo,Customer,InvoiceDate,DueDate,Item,Description,Quantity,Rate,Amount,TaxAmount,Currency");

        foreach (var inv in invoices)
        {
            var invDate = inv.GeneratedAt.UtcDateTime.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture);
            var dueDate = inv.DueDate.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture);

            foreach (var li in inv.LineItems)
            {
                sb.AppendLine(string.Join(",",
                    CsvEscape(inv.InvoiceNumber),
                    CsvEscape(inv.Project.Name),
                    invDate,
                    dueDate,
                    "Consulting Services",
                    CsvEscape($"{li.EmployeeName} - {li.Description}"),
                    li.Hours.ToString("0.00", CultureInfo.InvariantCulture),
                    li.HourlyRate.ToString("0.00", CultureInfo.InvariantCulture),
                    li.Amount.ToString("0.00", CultureInfo.InvariantCulture),
                    inv.TaxAmount.ToString("0.00", CultureInfo.InvariantCulture),
                    CsvEscape(inv.Currency)));
            }
        }

        return sb.ToString();
    }

    private static string BuildQuickBooksIif(IReadOnlyList<Invoice> invoices)
    {
        var sb = new StringBuilder();
        sb.AppendLine("!TRNS\tTRNSTYPE\tDATE\tACCNT\tNAME\tAMOUNT\tDOCNUM\tMEMO");
        sb.AppendLine("!SPL\tTRNSTYPE\tDATE\tACCNT\tNAME\tAMOUNT\tQNTY\tPRICE\tMEMO");
        sb.AppendLine("!ENDTRNS");

        foreach (var inv in invoices)
        {
            var invDate = inv.GeneratedAt.UtcDateTime.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture);
            sb.AppendLine($"TRNS\tINVOICE\t{invDate}\tAccounts Receivable\t{SanitizeTab(inv.Project.Name)}\t{inv.TotalAmount.ToString("0.00", CultureInfo.InvariantCulture)}\t{SanitizeTab(inv.InvoiceNumber)}\tMonthly Billing {inv.Year}-{inv.Month:D2}");

            foreach (var li in inv.LineItems)
            {
                var negAmount = (-li.Amount).ToString("0.00", CultureInfo.InvariantCulture);
                sb.AppendLine($"SPL\tINVOICE\t{invDate}\tConsulting Income\t{SanitizeTab(inv.Project.Name)}\t{negAmount}\t{(-li.Hours).ToString("0.00", CultureInfo.InvariantCulture)}\t{li.HourlyRate.ToString("0.00", CultureInfo.InvariantCulture)}\t{SanitizeTab(li.EmployeeName + " - " + li.Description)}");
            }

            if (inv.TaxAmount > 0)
            {
                sb.AppendLine($"SPL\tINVOICE\t{invDate}\tSales Tax Payable\tTax Agency\t{(-inv.TaxAmount).ToString("0.00", CultureInfo.InvariantCulture)}\t\t\tTax {inv.TaxRatePercent:0.##}%");
            }

            sb.AppendLine("ENDTRNS");
        }

        return sb.ToString();
    }

    private static string CsvEscape(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }
        return value;
    }

    private static string SanitizeTab(string? value) =>
        (value ?? string.Empty).Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
}
