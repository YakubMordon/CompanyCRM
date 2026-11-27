namespace CompanyCRM.Domain.Entities;

public enum InvoiceStatus
{
    Draft = 0,
    Sent = 1,
    Paid = 2,
    Void = 3
}

public class Invoice
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string InvoiceNumber { get; set; } = string.Empty;

    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public int Year { get; set; }
    public int Month { get; set; }
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Draft;

    public decimal TotalBillableHours { get; set; }
    public decimal SubtotalAmount { get; set; }
    public decimal TaxRatePercent { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public string Currency { get; set; } = "USD";

    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateOnly DueDate { get; set; }

    public ICollection<InvoiceLineItem> LineItems { get; set; } = new List<InvoiceLineItem>();
}

public class InvoiceLineItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid InvoiceId { get; set; }
    public Invoice Invoice { get; set; } = null!;

    public Guid EmployeeId { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public decimal Hours { get; set; }
    public decimal HourlyRate { get; set; }
    public decimal Amount { get; set; }
}
