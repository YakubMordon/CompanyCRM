namespace CompanyCRM.Application.DTOs;

public record ChatWebhookConfigDto(
    Guid Id,
    Guid? ProjectId,
    string? SlackWebhookUrl,
    string? TeamsWebhookUrl,
    bool IsCutoffAlertsEnabled,
    bool IsReviewAlertsEnabled,
    bool IsSimulatedMode,
    DateTimeOffset UpdatedAt
);

public record UpdateChatWebhookConfigDto(
    Guid? ProjectId,
    string? SlackWebhookUrl,
    string? TeamsWebhookUrl,
    bool IsCutoffAlertsEnabled,
    bool IsReviewAlertsEnabled,
    bool IsSimulatedMode
);

public record ChatWebhookTestResultDto(
    bool SlackDelivered,
    bool TeamsDelivered,
    bool IsSimulated,
    string Message
);
