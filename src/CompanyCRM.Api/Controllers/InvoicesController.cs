using CompanyCRM.Application.DTOs;
using CompanyCRM.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CompanyCRM.Api.Controllers;

[ApiController]
[Route("api/invoices")]
[Authorize(Policy = "HigherManagement")]
public class InvoicesController(IInvoiceService invoiceService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<InvoiceDto>>> GetInvoices(
        [FromQuery] Guid? projectId = null,
        [FromQuery] int? year = null,
        [FromQuery] int? month = null,
        CancellationToken ct = default)
    {
        var invoices = await invoiceService.GetInvoicesAsync(projectId, year, month, ct);
        return Ok(invoices);
    }

    [HttpPost("generate")]
    public async Task<ActionResult<IReadOnlyList<InvoiceDto>>> GenerateMonthlyInvoices(
        [FromBody] GenerateInvoicesRequestDto request,
        CancellationToken ct = default)
    {
        try
        {
            var generated = await invoiceService.GenerateMonthlyInvoicesAsync(
                request.Year,
                request.Month,
                request.ProjectId,
                request.TaxRatePercent,
                ct);
            return Ok(generated);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("{id:guid}/pdf")]
    public async Task<IActionResult> DownloadInvoicePdf(
        [FromRoute] Guid id,
        CancellationToken ct = default)
    {
        try
        {
            var (pdfBytes, fileName) = await invoiceService.RenderInvoicePdfAsync(id, ct);
            return File(pdfBytes, "application/pdf", fileName);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<InvoiceDto>> UpdateStatus(
        [FromRoute] Guid id,
        [FromBody] UpdateInvoiceStatusDto request,
        CancellationToken ct = default)
    {
        try
        {
            var updated = await invoiceService.UpdateInvoiceStatusAsync(id, request.Status, ct);
            return Ok(updated);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }
}
