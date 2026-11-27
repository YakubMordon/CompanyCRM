namespace CompanyCRM.Domain.Entities;

public class ClientRetainer
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public string ClientName { get; set; } = string.Empty;
    public decimal MonthlyRetainerHours { get; set; }
    public decimal MonthlyBudgetAmount { get; set; }
    public string Currency { get; set; } = "USD";

    /// <summary>Percentage (e.g. 80) at which a budget warning alarm is triggered.</summary>
    public decimal WarningThresholdPercent { get; set; } = 80m;

    public bool IsActive { get; set; } = true;
    public bool NotifySlackTeams { get; set; } = true;
    public bool NotifyEmail { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
