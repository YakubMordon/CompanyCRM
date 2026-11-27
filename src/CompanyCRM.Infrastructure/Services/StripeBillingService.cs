using System.Net.Http.Headers;
using System.Text.Json;
using CompanyCRM.Application.DTOs;
using CompanyCRM.Application.Interfaces;
using CompanyCRM.Domain.Entities;
using CompanyCRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CompanyCRM.Infrastructure.Services;

public class StripeBillingService(
    HttpClient httpClient,
    CrmDbContext dbContext,
    IConfiguration configuration,
    ILogger<StripeBillingService> logger) : IStripeBillingService
{
    public async Task<StripeCheckoutSessionDto> CreateCheckoutSessionAsync(
        Guid invoiceId,
        string? successUrl = null,
        string? cancelUrl = null,
        CancellationToken ct = default)
    {
        var invoice = await dbContext.Invoices
            .Include(i => i.Project)
            .Include(i => i.LineItems)
            .FirstOrDefaultAsync(i => i.Id == invoiceId, ct)
            ?? throw new KeyNotFoundException($"Invoice '{invoiceId}' not found.");

        if (invoice.Status == InvoiceStatus.Paid)
        {
            throw new InvalidOperationException("Invoice is already marked as Paid.");
        }

        if (invoice.Status == InvoiceStatus.Void)
        {
            throw new InvalidOperationException("Cannot create a Stripe checkout session for a Void invoice.");
        }

        var secretKey = configuration["Stripe:SecretKey"];
        if (string.IsNullOrWhiteSpace(secretKey))
        {
            // Deterministic simulated mode
            var simSessionId = $"cs_sim_{invoice.Id:N}"[..28];
            var simUrl = $"https://checkout.stripe.com/c/pay/{simSessionId}#simulated";

            invoice.StripeCheckoutSessionId = simSessionId;
            invoice.StripePaymentUrl = simUrl;
            if (invoice.Status == InvoiceStatus.Draft)
            {
                invoice.Status = InvoiceStatus.Sent;
            }
            await dbContext.SaveChangesAsync(ct);

            logger.LogInformation("[Stripe Simulator] Created simulated checkout session {SessionId} for invoice {InvoiceNumber}", simSessionId, invoice.InvoiceNumber);
            return new StripeCheckoutSessionDto(invoice.Id, simSessionId, simUrl, IsSimulated: true);
        }

        // Live Stripe Checkout Session API
        var resolvedSuccess = string.IsNullOrWhiteSpace(successUrl) ? "http://localhost:5173/management?stripe=success" : successUrl;
        var resolvedCancel = string.IsNullOrWhiteSpace(cancelUrl) ? "http://localhost:5173/management?stripe=cancel" : cancelUrl;

        var form = new List<KeyValuePair<string, string>>
        {
            new("mode", "payment"),
            new("success_url", resolvedSuccess),
            new("cancel_url", resolvedCancel),
            new("client_reference_id", invoice.Id.ToString()),
            new("metadata[invoice_id]", invoice.Id.ToString()),
            new("metadata[invoice_number]", invoice.InvoiceNumber),
            new("line_items[0][price_data][currency]", invoice.Currency.ToLowerInvariant()),
            new("line_items[0][price_data][unit_amount]", ((long)Math.Round(invoice.TotalAmount * 100m, MidpointRounding.AwayFromZero)).ToString()),
            new("line_items[0][price_data][product_data][name]", $"Invoice {invoice.InvoiceNumber} ({invoice.Project.Code})"),
            new("line_items[0][quantity]", "1")
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.stripe.com/v1/checkout/sessions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secretKey);
        request.Content = new FormUrlEncodedContent(form);

        using var response = await httpClient.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("Stripe API error ({Status}): {Body}", response.StatusCode, body);
            throw new InvalidOperationException($"Stripe Checkout creation failed: {response.StatusCode}");
        }

        using var doc = JsonDocument.Parse(body);
        var sessionId = doc.RootElement.GetProperty("id").GetString() ?? string.Empty;
        var checkoutUrl = doc.RootElement.GetProperty("url").GetString() ?? string.Empty;

        invoice.StripeCheckoutSessionId = sessionId;
        invoice.StripePaymentUrl = checkoutUrl;
        if (invoice.Status == InvoiceStatus.Draft)
        {
            invoice.Status = InvoiceStatus.Sent;
        }
        await dbContext.SaveChangesAsync(ct);

        return new StripeCheckoutSessionDto(invoice.Id, sessionId, checkoutUrl, IsSimulated: false);
    }

    public async Task<InvoiceDto> SimulatePaymentAsync(Guid invoiceId, CancellationToken ct = default)
    {
        var invoice = await dbContext.Invoices
            .Include(i => i.Project)
            .Include(i => i.LineItems)
            .FirstOrDefaultAsync(i => i.Id == invoiceId, ct)
            ?? throw new KeyNotFoundException($"Invoice '{invoiceId}' not found.");

        if (invoice.Status == InvoiceStatus.Void)
        {
            throw new InvalidOperationException("Cannot pay a Void invoice.");
        }

        invoice.StripeCheckoutSessionId ??= $"cs_sim_{invoice.Id:N}"[..28];
        invoice.StripePaymentIntentId ??= $"pi_sim_{invoice.Id:N}"[..28];
        invoice.Status = InvoiceStatus.Paid;
        invoice.PaidAt ??= DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(ct);
        logger.LogInformation("[Stripe Simulator] Marked invoice {InvoiceNumber} as Paid", invoice.InvoiceNumber);

        return InvoiceService.ToDto(invoice);
    }

    public async Task<InvoiceDto?> ProcessWebhookAsync(string payload, string? stripeSignature, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(payload)) return null;

        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;
        var eventType = root.TryGetProperty("type", out var typeProp) ? typeProp.GetString() : null;

        if (!string.Equals(eventType, "checkout.session.completed", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(eventType, "payment_intent.succeeded", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!root.TryGetProperty("data", out var dataProp) || !dataProp.TryGetProperty("object", out var objProp))
        {
            return null;
        }

        string? sessionId = objProp.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
        string? paymentIntentId = objProp.TryGetProperty("payment_intent", out var piProp) ? piProp.GetString() : null;
        Guid? invoiceId = null;

        if (objProp.TryGetProperty("client_reference_id", out var refProp) &&
            Guid.TryParse(refProp.GetString(), out var parsedRef))
        {
            invoiceId = parsedRef;
        }
        else if (objProp.TryGetProperty("metadata", out var metaProp) &&
                 metaProp.TryGetProperty("invoice_id", out var metaInvProp) &&
                 Guid.TryParse(metaInvProp.GetString(), out var parsedMeta))
        {
            invoiceId = parsedMeta;
        }

        var invoice = await dbContext.Invoices
            .Include(i => i.Project)
            .Include(i => i.LineItems)
            .FirstOrDefaultAsync(i =>
                (invoiceId.HasValue && i.Id == invoiceId.Value) ||
                (!string.IsNullOrEmpty(sessionId) && i.StripeCheckoutSessionId == sessionId), ct);

        if (invoice == null) return null;

        if (invoice.Status != InvoiceStatus.Paid)
        {
            invoice.Status = InvoiceStatus.Paid;
            invoice.PaidAt = DateTimeOffset.UtcNow;
            if (!string.IsNullOrWhiteSpace(paymentIntentId))
            {
                invoice.StripePaymentIntentId = paymentIntentId;
            }
            await dbContext.SaveChangesAsync(ct);
        }

        return InvoiceService.ToDto(invoice);
    }
}
