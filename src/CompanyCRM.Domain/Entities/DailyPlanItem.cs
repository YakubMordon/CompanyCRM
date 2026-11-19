namespace CompanyCRM.Domain.Entities;

public class DailyPlanItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid DailyPlanId { get; set; }
    public DailyPlan DailyPlan { get; set; } = null!;

    public Guid? WorkItemId { get; set; }
    public WorkItem? WorkItem { get; set; }

    public string? CustomTaskTitle { get; set; }
    public decimal PlannedHours { get; set; } = 1.0m;
    public string? GoalNote { get; set; }
    public bool IsCompleted { get; set; } = false;
}
