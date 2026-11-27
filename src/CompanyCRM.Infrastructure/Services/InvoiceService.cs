using CompanyCRM.Application.DTOs;
using CompanyCRM.Application.Interfaces;
using CompanyCRM.Domain.Entities;
using CompanyCRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CompanyCRM.Infrastructure.Services;

public class InvoiceService(CrmDbContext dbContext) : IInvoiceService
{
    public async Task<IReadOnlyList<InvoiceDto>> GetInvoicesAsync(
        Guid? projectId = null,
        int? year = null,
        int? month = null,
        CancellationToken ct = default)
    {
        var query = dbContext.Invoices
            .AsNoTracking()
            .Include(i => i.Project)
            .Include(i => i.LineItems)
            .AsQueryable();

        if (projectId.HasValue) query = query.Where(i => i.ProjectId == projectId.Value);
        if (year.HasValue) query = query.Where(i => i.Year == year.Value);
        if (month.HasValue) query = query.Where(i => i.Month == month.Value);

        var invoices = await query
            .OrderByDescending(i => i.Year)
            .ThenByDescending(i => i.Month)
            .ThenByDescending(i => i.GeneratedAt)
            .ToListAsync(ct);

        return [.. invoices.Select(ToDto)];
    }

    public async Task<InvoiceDto?> GetInvoiceByIdAsync(Guid invoiceId, CancellationToken ct = default)
    {
        var invoice = await dbContext.Invoices
            .AsNoTracking()
            .Include(i => i.Project)
            .Include(i => i.LineItems)
            .FirstOrDefaultAsync(i => i.Id == invoiceId, ct);

        return invoice == null ? null : ToDto(invoice);
    }

    public async Task<IReadOnlyList<InvoiceDto>> GenerateMonthlyInvoicesAsync(
        int year,
        int month,
        Guid? projectId = null,
        decimal taxRatePercent = 0m,
        CancellationToken ct = default)
    {
        ValidateMonthlyInvoice(month, taxRatePercent);

        var startDate = new DateOnly(year, month, 1);
        var endDate = startDate.AddMonths(1).AddDays(-1);

        var entriesQuery = dbContext.TimeEntries
            .Include(t => t.Employee)
            .Include(t => t.WorkItem)
                .ThenInclude(w => w.Project)
            .Where(t => !t.IsRunning && t.IsBillable && t.LoggedDate >= startDate && t.LoggedDate <= endDate);

        if (projectId.HasValue)
        {
            entriesQuery = entriesQuery.Where(t => t.WorkItem.ProjectId == projectId.Value);
        }

        var entries = await entriesQuery.ToListAsync(ct);
        var groupedByProject = entries.GroupBy(t => t.WorkItem.Project);
        var generated = new List<Invoice>();

        foreach (var projectGroup in groupedByProject)
        {
            var project = projectGroup.Key;
            var existing = await dbContext.Invoices
                .Include(i => i.Project)
                .Include(i => i.LineItems)
                .FirstOrDefaultAsync(i => i.ProjectId == project.Id && i.Year == year && i.Month == month, ct);

            if (existing != null && existing.Status != InvoiceStatus.Draft)
            {
                generated.Add(existing);
                continue;
            }

            var lineItems = projectGroup
                .GroupBy(t => t.Employee)
                .Select(empGroup =>
                {
                    var emp = empGroup.Key;
                    var totalMinutes = empGroup.Sum(t => t.DurationMinutes);
                    var hours = Math.Round((decimal)totalMinutes / 60m, 2, MidpointRounding.AwayFromZero);
                    var rate = emp.HourlyBillableRate;
                    var amount = Math.Round(hours * rate, 2, MidpointRounding.AwayFromZero);
                    return new InvoiceLineItem
                    {
                        Id = Guid.NewGuid(),
                        EmployeeId = emp.Id,
                        EmployeeName = emp.FullName,
                        Description = $"Billable engineering hours ({year}-{month:D2})",
                        Hours = hours,
                        HourlyRate = rate,
                        Amount = amount
                    };
                })
                .Where(li => li.Hours > 0)
                .OrderBy(li => li.EmployeeName)
                .ToList();

            if (lineItems.Count == 0) continue;

            var totalHours = lineItems.Sum(li => li.Hours);
            var subtotal = lineItems.Sum(li => li.Amount);
            var taxAmount = Math.Round(subtotal * (taxRatePercent / 100m), 2, MidpointRounding.AwayFromZero);
            var totalAmount = subtotal + taxAmount;

            if (existing == null)
            {
                existing = new Invoice
                {
                    Id = Guid.NewGuid(),
                    InvoiceNumber = $"INV-{year}{month:D2}-{project.Code}",
                    ProjectId = project.Id,
                    Project = project,
                    Year = year,
                    Month = month,
                    Status = InvoiceStatus.Draft,
                    Currency = "USD",
                    DueDate = endDate.AddDays(30)
                };
                dbContext.Invoices.Add(existing);
            }
            else
            {
                dbContext.InvoiceLineItems.RemoveRange(existing.LineItems);
                existing.LineItems.Clear();
            }

            existing.TotalBillableHours = totalHours;
            existing.SubtotalAmount = subtotal;
            existing.TaxRatePercent = taxRatePercent;
            existing.TaxAmount = taxAmount;
            existing.TotalAmount = totalAmount;
            existing.GeneratedAt = DateTimeOffset.UtcNow;

            foreach (var li in lineItems)
            {
                li.InvoiceId = existing.Id;
                existing.LineItems.Add(li);
            }

            generated.Add(existing);
        }

        await dbContext.SaveChangesAsync(ct);
        return [.. generated.Select(ToDto)];
    }

    private static void ValidateMonthlyInvoice(int month, decimal taxRatePercent)
    {
        if (month < 1 || month > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(month), "Month must be between 1 and 12.");
        }

        if (taxRatePercent < 0m || taxRatePercent > 100m)
        {
            throw new ArgumentOutOfRangeException(nameof(taxRatePercent), "Tax rate percent must be between 0 and 100.");
        }
    }

    public async Task<InvoiceDto> UpdateInvoiceStatusAsync(
        Guid invoiceId,
        InvoiceStatus status,
        CancellationToken ct = default)
    {
        var invoice = await dbContext.Invoices
            .Include(i => i.Project)
            .Include(i => i.LineItems)
            .FirstOrDefaultAsync(i => i.Id == invoiceId, ct)
            ?? throw new KeyNotFoundException($"Invoice '{invoiceId}' not found.");

        invoice.Status = status;
        await dbContext.SaveChangesAsync(ct);
        return ToDto(invoice);
    }

    public async Task<(byte[] PdfBytes, string FileName)> RenderInvoicePdfAsync(
        Guid invoiceId,
        CancellationToken ct = default)
    {
        var invoice = await dbContext.Invoices
            .AsNoTracking()
            .Include(i => i.Project)
            .Include(i => i.LineItems)
            .FirstOrDefaultAsync(i => i.Id == invoiceId, ct)
            ?? throw new KeyNotFoundException($"Invoice '{invoiceId}' not found.");

        var pdfBytes = PdfInvoiceRenderer.Render(invoice);
        var fileName = $"{invoice.InvoiceNumber}.pdf";
        return (pdfBytes, fileName);
    }

    private static InvoiceDto ToDto(Invoice i) => new(
        i.Id,
        i.InvoiceNumber,
        i.ProjectId,
        i.Project?.Name ?? string.Empty,
        i.Project?.Code ?? string.Empty,
        i.Year,
        i.Month,
        i.Status,
        i.TotalBillableHours,
        i.SubtotalAmount,
        i.TaxRatePercent,
        i.TaxAmount,
        i.TotalAmount,
        i.Currency,
        i.GeneratedAt,
        i.DueDate,
        [.. i.LineItems.Select(li => new InvoiceLineItemDto(
            li.Id,
            li.EmployeeId,
            li.EmployeeName,
            li.Description,
            li.Hours,
            li.HourlyRate,
            li.Amount))]);
}
