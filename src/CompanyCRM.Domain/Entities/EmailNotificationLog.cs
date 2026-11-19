namespace CompanyCRM.Domain.Entities;

public class EmailNotificationLog
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string RecipientEmail { get; set; } = string.Empty;
    public string RecipientName { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string BodyHtml { get; set; } = string.Empty;
    public string TriggerEvent { get; set; } = string.Empty; // e.g. "ReviewerAssigned", "PlanningCutoffReminder"

    public Guid? WorkItemId { get; set; }
    public WorkItem? WorkItem { get; set; }

    public DateTimeOffset SentAt { get; set; } = DateTimeOffset.UtcNow;
    public bool Success { get; set; } = true;
    public string? ErrorMessage { get; set; }
}
