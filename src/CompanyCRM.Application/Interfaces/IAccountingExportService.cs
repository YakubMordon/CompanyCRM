namespace CompanyCRM.Application.Interfaces;

using CompanyCRM.Application.DTOs;

public interface IAccountingExportService
{
    Task<(byte[] FileBytes, string ContentType, string FileName)> ExportInvoicesAsync(
        AccountingExportRequestDto request,
        CancellationToken ct = default);
}
