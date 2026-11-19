namespace CompanyCRM.Domain.Entities;

public class DailyPlanReminderLog
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;

    public Guid? ProjectId { get; set; }

    public DateOnly PlanDate { get; set; }
    public DateTimeOffset ReminderSentAt { get; set; } = DateTimeOffset.UtcNow;
    public string RecipientEmail { get; set; } = string.Empty;
    public string EmailDeliveryStatus { get; set; } = "Sent"; // "Sent" or "Failed"
    public string? ErrorMessage { get; set; }
}
