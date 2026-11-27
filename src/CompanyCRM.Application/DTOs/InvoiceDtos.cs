namespace CompanyCRM.Application.DTOs;

using CompanyCRM.Domain.Entities;

public record InvoiceLineItemDto(
    Guid Id,
    Guid EmployeeId,
    string EmployeeName,
    string Description,
    decimal Hours,
    decimal HourlyRate,
    decimal Amount
);

public record InvoiceDto(
    Guid Id,
    string InvoiceNumber,
    Guid ProjectId,
    string ProjectName,
    string ProjectCode,
    int Year,
    int Month,
    InvoiceStatus Status,
    decimal TotalBillableHours,
    decimal SubtotalAmount,
    decimal TaxRatePercent,
    decimal TaxAmount,
    decimal TotalAmount,
    string Currency,
    DateTimeOffset GeneratedAt,
    DateOnly DueDate,
    IReadOnlyList<InvoiceLineItemDto> LineItems,
    string? StripeCheckoutSessionId = null,
    string? StripePaymentUrl = null,
    DateTimeOffset? PaidAt = null,
    string? LastExportedProvider = null,
    DateTimeOffset? LastExportedAt = null
);

public record GenerateInvoicesRequestDto(
    int Year,
    int Month,
    Guid? ProjectId = null,
    decimal TaxRatePercent = 0m
);

public record UpdateInvoiceStatusDto(
    InvoiceStatus Status
);

public record CreateStripeCheckoutRequestDto(
    string? SuccessUrl = null,
    string? CancelUrl = null
);

public record StripeCheckoutSessionDto(
    Guid InvoiceId,
    string SessionId,
    string CheckoutUrl,
    bool IsSimulated
);

public enum AccountingExportFormat
{
    XeroCsv = 0,
    QuickBooksCsv = 1,
    QuickBooksIif = 2
}

public record AccountingExportRequestDto(
    AccountingExportFormat Format,
    Guid? ProjectId = null,
    int? Year = null,
    int? Month = null
);
