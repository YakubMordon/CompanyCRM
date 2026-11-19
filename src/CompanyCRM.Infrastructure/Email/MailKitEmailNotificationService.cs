using CompanyCRM.Application.Interfaces;
using CompanyCRM.Domain.Entities;
using CompanyCRM.Domain.Enums;
using CompanyCRM.Infrastructure.Persistence;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace CompanyCRM.Infrastructure.Email;

public class MailKitEmailNotificationService : IEmailNotificationService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<MailKitEmailNotificationService> _logger;

    public MailKitEmailNotificationService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<MailKitEmailNotificationService> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task SendReviewAssignedEmailAsync(
        string recipientEmail,
        string recipientName,
        string workItemCode,
        string workItemTitle,
        Guid workItemId,
        CancellationToken ct = default)
    {
        var subject = $"[CompanyCRM] Review Requested: {workItemCode} - {workItemTitle}";
        var bodyHtml = EmailTemplates.GenerateReviewAssignedHtml(recipientName, workItemCode, workItemTitle, workItemId);

        await DispatchEmailWithLoggingAsync(
            recipientEmail,
            recipientName,
            subject,
            bodyHtml,
            triggerEvent: "ReviewerAssigned",
            workItemId: workItemId,
            ct: ct);
    }

    public async Task SendReviewDecisionEmailAsync(
        string recipientEmail,
        string recipientName,
        string workItemCode,
        string workItemTitle,
        ReviewStatus status,
        string? comment,
        CancellationToken ct = default)
    {
        var statusLabel = status switch
        {
            ReviewStatus.Approved => "Approved",
            ReviewStatus.ClarificationNeeded => "Clarification Requested",
            ReviewStatus.Denied => "Denied",
            _ => "Updated"
        };

        var subject = $"[CompanyCRM] Review {statusLabel}: {workItemCode} - {workItemTitle}";
        var bodyHtml = EmailTemplates.GenerateReviewDecisionHtml(recipientName, workItemCode, workItemTitle, status, comment);

        await DispatchEmailWithLoggingAsync(
            recipientEmail,
            recipientName,
            subject,
            bodyHtml,
            triggerEvent: $"Review{statusLabel.Replace(" ", "")}",
            workItemId: null,
            ct: ct);
    }

    public async Task SendCutoffReminderEmailAsync(
        string recipientEmail,
        string recipientName,
        DateOnly planDate,
        TimeOnly cutoffTime,
        CancellationToken ct = default)
    {
        var subject = $"[CompanyCRM] Action Required: Missing Daily Plan for {planDate:yyyy-MM-dd}";
        var bodyHtml = EmailTemplates.GenerateCutoffReminderHtml(recipientName, planDate, cutoffTime);

        await DispatchEmailWithLoggingAsync(
            recipientEmail,
            recipientName,
            subject,
            bodyHtml,
            triggerEvent: "DailyPlanCutoffReminder",
            workItemId: null,
            ct: ct);
    }

    private async Task DispatchEmailWithLoggingAsync(
        string recipientEmail,
        string recipientName,
        string subject,
        string bodyHtml,
        string triggerEvent,
        Guid? workItemId,
        CancellationToken ct)
    {
        var host = _configuration["Smtp:Host"] ?? "localhost";
        var portStr = _configuration["Smtp:Port"] ?? "1025";
        int.TryParse(portStr, out var port);
        if (port <= 0) port = 1025;

        var senderEmail = _configuration["Smtp:SenderEmail"] ?? "no-reply@companycrm.local";
        var senderName = _configuration["Smtp:SenderName"] ?? "CompanyCRM Notifications";

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(senderName, senderEmail));
        message.To.Add(new MailboxAddress(recipientName, recipientEmail));
        message.Subject = subject;

        var builder = new BodyBuilder { HtmlBody = bodyHtml };
        message.Body = builder.ToMessageBody();

        bool success = false;
        string? errorMessage = null;

        try
        {
            using var client = new SmtpClient();
            client.Timeout = 5000; // 5-second socket timeout

            // Connect to MailPit or local SMTP server
            await client.ConnectAsync(host, port, SecureSocketOptions.None, ct);

            var username = _configuration["Smtp:Username"];
            var password = _configuration["Smtp:Password"];
            if (!string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(password))
            {
                await client.AuthenticateAsync(username, password, ct);
            }

            await client.SendAsync(message, ct);
            await client.DisconnectAsync(true, ct);

            success = true;
            _logger.LogInformation("Successfully dispatched email '{Subject}' to {RecipientEmail} via SMTP {Host}:{Port}",
                subject, recipientEmail, host, port);
        }
        catch (Exception ex)
        {
            success = false;
            errorMessage = ex.Message;
            _logger.LogWarning(ex, "Failed to deliver email '{Subject}' to {RecipientEmail} via SMTP {Host}:{Port}. Gracefully falling back to database log.",
                subject, recipientEmail, host, port);
            // CRITICAL: Graceful fallback! Do NOT rethrow or rollback outer transaction.
        }

        // Persist email audit record in an isolated DbContext scope
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<CrmDbContext>();

            Guid? validWorkItemId = null;
            if (workItemId.HasValue && await dbContext.WorkItems.AnyAsync(w => w.Id == workItemId.Value, ct))
            {
                validWorkItemId = workItemId.Value;
            }

            var logEntry = new EmailNotificationLog
            {
                Id = Guid.NewGuid(),
                RecipientEmail = recipientEmail,
                RecipientName = recipientName,
                Subject = subject,
                BodyHtml = bodyHtml,
                TriggerEvent = triggerEvent,
                WorkItemId = validWorkItemId,
                SentAt = DateTimeOffset.UtcNow,
                Success = success,
                ErrorMessage = errorMessage
            };

            dbContext.EmailNotificationLogs.Add(logEntry);
            await dbContext.SaveChangesAsync(ct);
        }
        catch (Exception logEx)
        {
            _logger.LogError(logEx, "Failed to persist EmailNotificationLog for {RecipientEmail}", recipientEmail);
        }
    }
}
