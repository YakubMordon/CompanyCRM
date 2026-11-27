namespace CompanyCRM.Application.Interfaces;

using CompanyCRM.Application.DTOs;
using CompanyCRM.Domain.Entities;

public interface IInvoiceService
{
    Task<IReadOnlyList<InvoiceDto>> GetInvoicesAsync(
        Guid? projectId = null,
        int? year = null,
        int? month = null,
        CancellationToken ct = default);

    Task<InvoiceDto?> GetInvoiceByIdAsync(Guid invoiceId, CancellationToken ct = default);

    Task<IReadOnlyList<InvoiceDto>> GenerateMonthlyInvoicesAsync(
        int year,
        int month,
        Guid? projectId = null,
        decimal taxRatePercent = 0m,
        CancellationToken ct = default);

    Task<InvoiceDto> UpdateInvoiceStatusAsync(
        Guid invoiceId,
        InvoiceStatus status,
        CancellationToken ct = default);

    Task<(byte[] PdfBytes, string FileName)> RenderInvoicePdfAsync(
        Guid invoiceId,
        CancellationToken ct = default);
}
