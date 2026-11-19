using CompanyCRM.Domain.Common;
using CompanyCRM.Domain.Enums;

namespace CompanyCRM.Domain.Entities;

public class WeeklyPlan : BaseEntity<Guid>
{
    public WeeklyPlan()
    {
        Id = Guid.NewGuid();
    }

    public Guid EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;

    public Guid? ProjectId { get; set; }
    public Project? Project { get; set; }

    /// <summary>
    /// Canonical start of the week, normalized to Monday.
    /// </summary>
    public DateOnly WeekStartDate { get; set; }
    public string GoalsMarkdown { get; set; } = string.Empty;
    public string? FocusAreasJson { get; set; }
    public string? KeyDeliverables { get; set; }
    public WeeklyPlanStatus Status { get; set; } = WeeklyPlanStatus.Draft;

    public static DateOnly NormalizeToMonday(DateOnly date)
    {
        var dayOfWeek = date.DayOfWeek;
        var diff = (7 + (dayOfWeek - DayOfWeek.Monday)) % 7;
        return date.AddDays(-diff);
    }
}
