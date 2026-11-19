namespace CompanyCRM.Domain.Entities;

public class WorkItemHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid WorkItemId { get; set; }
    public WorkItem WorkItem { get; set; } = null!;

    public Guid ChangedById { get; set; }
    public Employee ChangedBy { get; set; } = null!;

    public DateTimeOffset ChangedAt { get; set; } = DateTimeOffset.UtcNow;
    public string Action { get; set; } = string.Empty; // e.g., "Created", "StatusChanged", "ReviewApproved"
    public string? FieldName { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string? Note { get; set; }
}
