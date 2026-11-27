using CompanyCRM.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CompanyCRM.Api.Controllers;

[ApiController]
[Route("api/webhooks/stripe")]
[AllowAnonymous]
public class StripeWebhookController(IStripeBillingService stripeBillingService) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> HandleWebhook(
        [FromHeader(Name = "Stripe-Signature")] string signature, 
        CancellationToken ct = default)
    {
        using var reader = new StreamReader(Request.Body);
        var payload = await reader.ReadToEndAsync(ct);

        var updatedInvoice = await stripeBillingService.ProcessWebhookAsync(payload, signature, ct);
        return Ok(new
        {
            received = true,
            invoiceId = updatedInvoice?.Id,
            status = updatedInvoice?.Status.ToString()
        });
    }
}
