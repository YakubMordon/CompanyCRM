namespace CompanyCRM.Application.Interfaces;

using CompanyCRM.Application.DTOs;
using CompanyCRM.Domain.Enums;

public interface IChatWebhookNotificationService
{
    Task SendCutoffReminderAlertAsync(
        string employeeName,
        string employeeEmail,
        DateOnly planDate,
        TimeOnly cutoffTime,
        Guid? projectId = null,
        CancellationToken ct = default);

    Task SendReviewAssignedAlertAsync(
        string reviewerName,
        string workItemCode,
        string workItemTitle,
        Guid workItemId,
        Guid? projectId = null,
        CancellationToken ct = default);

    Task SendReviewDecisionAlertAsync(
        string creatorName,
        string workItemCode,
        string workItemTitle,
        ReviewStatus status,
        string? comment,
        Guid? projectId = null,
        CancellationToken ct = default);

    Task<ChatWebhookConfigDto> GetConfigAsync(
        Guid? projectId = null,
        CancellationToken ct = default);

    Task<ChatWebhookConfigDto> UpsertConfigAsync(
        UpdateChatWebhookConfigDto request,
        CancellationToken ct = default);

    Task<ChatWebhookTestResultDto> SendTestAlertAsync(
        Guid? projectId = null,
        CancellationToken ct = default);
}
