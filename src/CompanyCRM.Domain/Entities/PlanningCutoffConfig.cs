namespace CompanyCRM.Domain.Entities;

public class PlanningCutoffConfig
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid? ProjectId { get; set; } // null = Global default config
    public Project? Project { get; set; }

    public TimeOnly CutoffTimeLocal { get; set; } = new TimeOnly(10, 0, 0); // 10:00 AM
    public string TimeZoneId { get; set; } = "UTC";
    public bool IsReminderEnabled { get; set; } = true;
    public string ExcludedDaysOfWeekJson { get; set; } = "[0, 6]"; // Sunday=0, Saturday=6
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
