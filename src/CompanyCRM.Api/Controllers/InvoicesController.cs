using CompanyCRM.Application.DTOs;
using CompanyCRM.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CompanyCRM.Api.Controllers;

[ApiController]
[Route("api/invoices")]
[Authorize(Policy = "HigherManagement")]
public class InvoicesController(
    IInvoiceService invoiceService,
    IStripeBillingService stripeBillingService,
    IAccountingExportService accountingExportService) : ControllerBase
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

    [HttpPost("{id:guid}/stripe/checkout")]
    public async Task<ActionResult<StripeCheckoutSessionDto>> CreateStripeCheckout(
        [FromRoute] Guid id,
        [FromBody] CreateStripeCheckoutRequestDto? request = null,
        CancellationToken ct = default)
    {
        try
        {
            var session = await stripeBillingService.CreateCheckoutSessionAsync(
                id,
                request?.SuccessUrl,
                request?.CancelUrl,
                ct);
            return Ok(session);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{id:guid}/stripe/simulate-payment")]
    public async Task<ActionResult<InvoiceDto>> SimulateStripePayment(
        [FromRoute] Guid id,
        CancellationToken ct = default)
    {
        try
        {
            var updated = await stripeBillingService.SimulatePaymentAsync(id, ct);
            return Ok(updated);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("export")]
    public async Task<IActionResult> ExportInvoices(
        [FromBody] AccountingExportRequestDto request,
        CancellationToken ct = default)
    {
        var (fileBytes, contentType, fileName) = await accountingExportService.ExportInvoicesAsync(request, ct);
        return File(fileBytes, contentType, fileName);
    }
}
