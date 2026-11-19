namespace CompanyCRM.Application.Interfaces;

using CompanyCRM.Domain.Enums;

public interface IEmailNotificationService
{
    /// <summary>Sends an HTML email notification to a reviewer when a work item is assigned for review.</summary>
    Task SendReviewAssignedEmailAsync(string recipientEmail, string recipientName, string workItemCode, string workItemTitle, Guid workItemId, CancellationToken ct = default);

    /// <summary>Sends an HTML email notification to a creator when their item is approved, denied, or needs clarification.</summary>
    Task SendReviewDecisionEmailAsync(string recipientEmail, string recipientName, string workItemCode, string workItemTitle, ReviewStatus status, string? comment, CancellationToken ct = default);

    /// <summary>Sends an automated HTML reminder email to an employee with a missing daily plan past the cutoff deadline.</summary>
    Task SendCutoffReminderEmailAsync(string recipientEmail, string recipientName, DateOnly planDate, TimeOnly cutoffTime, CancellationToken ct = default);
}
