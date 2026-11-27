namespace CompanyCRM.Domain.Entities;

public enum BudgetAlertLevel
{
    Warning = 0,
    Exceeded = 1
}

public class BudgetOverageAlertLog
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public int Year { get; set; }
    public int Month { get; set; }
    public BudgetAlertLevel AlertLevel { get; set; }

    public decimal UtilizationPercent { get; set; }
    public decimal LoggedAmount { get; set; }
    public decimal BudgetAmount { get; set; }
    public DateTimeOffset SentAt { get; set; } = DateTimeOffset.UtcNow;
}
