namespace CompanyCRM.Application.Interfaces;

using CompanyCRM.Application.DTOs;

public interface IStripeBillingService
{
    Task<StripeCheckoutSessionDto> CreateCheckoutSessionAsync(
        Guid invoiceId,
        string? successUrl = null,
        string? cancelUrl = null,
        CancellationToken ct = default);

    Task<InvoiceDto> SimulatePaymentAsync(
        Guid invoiceId,
        CancellationToken ct = default);

    Task<InvoiceDto?> ProcessWebhookAsync(
        string payload,
        string? stripeSignature,
        CancellationToken ct = default);
}
