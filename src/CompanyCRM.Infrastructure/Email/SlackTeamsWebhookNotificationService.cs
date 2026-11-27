using System.Text;
using System.Text.Json;
using CompanyCRM.Application.DTOs;
using CompanyCRM.Application.Interfaces;
using CompanyCRM.Domain.Entities;
using CompanyCRM.Domain.Enums;
using CompanyCRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CompanyCRM.Infrastructure.Email;

public class SlackTeamsWebhookNotificationService(
    HttpClient httpClient,
    IServiceScopeFactory scopeFactory,
    ILogger<SlackTeamsWebhookNotificationService> logger) : IChatWebhookNotificationService
{
    public async Task SendCutoffReminderAlertAsync(
        string employeeName,
        string employeeEmail,
        DateOnly planDate,
        TimeOnly cutoffTime,
        Guid? projectId = null,
        CancellationToken ct = default)
    {
        var config = await ResolveConfigAsync(projectId, ct);
        if (config == null || !config.IsCutoffAlertsEnabled) return;

        var title = "Daily Plan Cutoff Reminder";
        var summary = $"{employeeName} ({employeeEmail}) has not submitted a daily plan for {planDate:yyyy-MM-dd} (cutoff {cutoffTime:HH:mm}).";

        await DispatchAsync(config, title, summary, ReviewStatusResult.Warning, ct);
    }

    public async Task SendReviewAssignedAlertAsync(
        string reviewerName,
        string workItemCode,
        string workItemTitle,
        Guid workItemId,
        Guid? projectId = null,
        CancellationToken ct = default)
    {
        var config = await ResolveConfigAsync(projectId, ct);
        if (config == null || !config.IsReviewAlertsEnabled) return;

        var title = $"Review Requested: {workItemCode}";
        var summary = $"{reviewerName} was assigned to review '{workItemTitle}' ({workItemCode}).";

        await DispatchAsync(config, title, summary, "info", ct);
    }

    public async Task SendReviewDecisionAlertAsync(
        string creatorName,
        string workItemCode,
        string workItemTitle,
        ReviewStatus status,
        string? comment,
        Guid? projectId = null,
        CancellationToken ct = default)
    {
        var config = await ResolveConfigAsync(projectId, ct);
        if (config == null || !config.IsReviewAlertsEnabled) return;

        var statusLabel = status switch
        {
            ReviewStatus.Approved => "Approved",
            ReviewStatus.ClarificationNeeded => "Clarification Requested",
            ReviewStatus.Denied => "Denied",
            _ => status.ToString()
        };

        var title = $"Review {statusLabel}: {workItemCode}";
        var summary = $"{creatorName}'s work item '{workItemTitle}' ({workItemCode}) was marked {statusLabel}."
            + (string.IsNullOrWhiteSpace(comment) ? string.Empty : $" Note: {comment}");

        var severity = status switch
        {
            ReviewStatus.Denied => ReviewStatusResult.Danger,
            ReviewStatus.Approved => ReviewStatusResult.Good,
            _ => ReviewStatusResult.Warning
        };
        await DispatchAsync(config, title, summary, severity, ct);
    }

    public async Task SendBudgetOverageAlertAsync(
        string projectCode,
        string projectName,
        string clientName,
        decimal utilizationPercent,
        decimal loggedAmount,
        decimal budgetAmount,
        string currency,
        bool isExceeded,
        Guid projectId,
        CancellationToken ct = default)
    {
        var config = await ResolveConfigAsync(projectId, ct);
        if (config == null) return;

        var title = isExceeded
            ? $"Budget Overage Alarm: {projectCode}"
            : $"Budget Warning Threshold: {projectCode}";
        var summary = $"{projectName} ({clientName}) has reached {utilizationPercent:0.0}% of its monthly retainer ({loggedAmount:N2} / {budgetAmount:N2} {currency}).";

        await DispatchAsync(config, title, summary, isExceeded ? ReviewStatusResult.Danger : ReviewStatusResult.Warning, ct);
    }

    public async Task<ChatWebhookConfigDto> GetConfigAsync(
        Guid? projectId = null,
        CancellationToken ct = default)
    {
        var config = await ResolveConfigAsync(projectId, ct);
        if (config == null)
        {
            return new ChatWebhookConfigDto(
                Id: Guid.Empty,
                ProjectId: projectId,
                SlackWebhookUrl: null,
                TeamsWebhookUrl: null,
                IsCutoffAlertsEnabled: true,
                IsReviewAlertsEnabled: true,
                IsSimulatedMode: true,
                UpdatedAt: DateTimeOffset.UtcNow);
        }

        return new ChatWebhookConfigDto(
            config.Id,
            config.ProjectId,
            config.SlackWebhookUrl,
            config.TeamsWebhookUrl,
            config.IsCutoffAlertsEnabled,
            config.IsReviewAlertsEnabled,
            config.IsSimulatedMode,
            config.UpdatedAt);
    }

    public async Task<ChatWebhookConfigDto> UpsertConfigAsync(
        UpdateChatWebhookConfigDto request,
        CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CrmDbContext>();

        var config = await dbContext.ChatWebhookConfigs
            .FirstOrDefaultAsync(c => c.ProjectId == request.ProjectId, ct);

        if (config == null)
        {
            config = new ChatWebhookConfig
            {
                ProjectId = request.ProjectId,
                SlackWebhookUrl = request.SlackWebhookUrl?.Trim(),
                TeamsWebhookUrl = request.TeamsWebhookUrl?.Trim(),
                IsCutoffAlertsEnabled = request.IsCutoffAlertsEnabled,
                IsReviewAlertsEnabled = request.IsReviewAlertsEnabled,
                IsSimulatedMode = request.IsSimulatedMode,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            dbContext.ChatWebhookConfigs.Add(config);
        }
        else
        {
            config.SlackWebhookUrl = request.SlackWebhookUrl?.Trim();
            config.TeamsWebhookUrl = request.TeamsWebhookUrl?.Trim();
            config.IsCutoffAlertsEnabled = request.IsCutoffAlertsEnabled;
            config.IsReviewAlertsEnabled = request.IsReviewAlertsEnabled;
            config.IsSimulatedMode = request.IsSimulatedMode;
            config.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await dbContext.SaveChangesAsync(ct);

        return new ChatWebhookConfigDto(
            config.Id,
            config.ProjectId,
            config.SlackWebhookUrl,
            config.TeamsWebhookUrl,
            config.IsCutoffAlertsEnabled,
            config.IsReviewAlertsEnabled,
            config.IsSimulatedMode,
            config.UpdatedAt);
    }

    public async Task<ChatWebhookTestResultDto> SendTestAlertAsync(
        Guid? projectId = null,
        CancellationToken ct = default)
    {
        var config = await ResolveConfigAsync(projectId, ct) ?? new ChatWebhookConfig
        {
            ProjectId = projectId,
            IsSimulatedMode = true
        };

        var (slackOk, teamsOk) = await DispatchAsync(
            config,
            "CompanyCRM Test Alert",
            "This is a test notification from CompanyCRM Slack & Microsoft Teams webhook integration.",
            ReviewStatusResult.Good,
            ct);

        var slackStatus = slackOk ? "OK" : "Skipped/Failed";
        var teamsStatus = teamsOk ? "OK" : "Skipped/Failed";

        var message = config.IsSimulatedMode
            ? "Simulated Slack & Microsoft Teams test alert logged (no external HTTP calls made)."
            : $"Test alert dispatched (Slack: {slackStatus}, Teams: {teamsStatus}).";

        return new ChatWebhookTestResultDto(slackOk, teamsOk, config.IsSimulatedMode, message);
    }

    private async Task<ChatWebhookConfig?> ResolveConfigAsync(Guid? projectId, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CrmDbContext>();

        ChatWebhookConfig? config = null;
        if (projectId.HasValue)
        {
            config = await dbContext.ChatWebhookConfigs
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.ProjectId == projectId.Value, ct);
        }

        config ??= await dbContext.ChatWebhookConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.ProjectId == null, ct);

        return config;
    }

    private async Task<(bool SlackOk, bool TeamsOk)> DispatchAsync(
        ChatWebhookConfig config,
        string title,
        string summary,
        string severity,
        CancellationToken ct)
    {
        var slackPayload = BuildSlackBlockKitPayload(title, summary);
        var teamsPayload = BuildTeamsMessageCardPayload(title, summary, severity);

        if (config.IsSimulatedMode)
        {
            logger.LogInformation(
                "[Simulated Chat Webhook] {Title} | SlackPayload={SlackPayload} | TeamsPayload={TeamsPayload}",
                title, slackPayload, teamsPayload);
            return (true, true);
        }

        var slackOk = false;
        if (!string.IsNullOrWhiteSpace(config.SlackWebhookUrl))
        {
            slackOk = await PostJsonAsync(config.SlackWebhookUrl, slackPayload, "Slack", ct);
        }

        var teamsOk = false;
        if (!string.IsNullOrWhiteSpace(config.TeamsWebhookUrl))
        {
            teamsOk = await PostJsonAsync(config.TeamsWebhookUrl, teamsPayload, "Teams", ct);
        }

        return (slackOk, teamsOk);
    }

    private async Task<bool> PostJsonAsync(string url, string jsonPayload, string channelName, CancellationToken ct)
    {
        try
        {
            using var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
            using var response = await httpClient.PostAsync(url, content, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("{Channel} webhook returned {StatusCode}", channelName, response.StatusCode);
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to dispatch {Channel} webhook", channelName);
            return false;
        }
    }

    public static string BuildSlackBlockKitPayload(string title, string summary)
    {
        var payload = new
        {
            text = $"{title}: {summary}",
            blocks = new object[]
            {
                new
                {
                    type = "header",
                    text = new { type = "plain_text", text = title }
                },
                new
                {
                    type = "section",
                    text = new { type = "mrkdwn", text = summary }
                }
            }
        };
        return JsonSerializer.Serialize(payload);
    }

    public static string BuildTeamsMessageCardPayload(string title, string summary, string severity)
    {
        var themeColor = severity switch
        {
            ReviewStatusResult.Danger => "D13438",
            ReviewStatusResult.Warning => "FFB900",
            ReviewStatusResult.Good => "107C10",
            _ => "0078D7"
        };

        var payload = new Dictionary<string, object>
        {
            ["@type"] = "MessageCard",
            ["@context"] = "http://schema.org/extensions",
            ["themeColor"] = themeColor,
            ["summary"] = title,
            ["title"] = title,
            ["text"] = summary
        };
        return JsonSerializer.Serialize(payload);
    }
}
