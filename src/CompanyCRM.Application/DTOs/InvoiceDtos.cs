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
    IReadOnlyList<InvoiceLineItemDto> LineItems
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
