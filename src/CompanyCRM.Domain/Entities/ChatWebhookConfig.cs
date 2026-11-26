namespace CompanyCRM.Domain.Entities;

public class ChatWebhookConfig
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid? ProjectId { get; set; } // null = Global default config
    public Project? Project { get; set; }

    public string? SlackWebhookUrl { get; set; }
    public string? TeamsWebhookUrl { get; set; }

    public bool IsCutoffAlertsEnabled { get; set; } = true;
    public bool IsReviewAlertsEnabled { get; set; } = true;
    public bool IsSimulatedMode { get; set; } = true;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
