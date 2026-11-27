using CompanyCRM.Application.DTOs;
using CompanyCRM.Application.Interfaces;
using CompanyCRM.Domain.Entities;
using CompanyCRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CompanyCRM.Infrastructure.Services;

public class RetainerService(
    CrmDbContext dbContext,
    IChatWebhookNotificationService chatWebhookService,
    ILogger<RetainerService> logger) : IRetainerService
{
    public async Task<IReadOnlyList<RetainerStatusDto>> GetRetainersAsync(
        Guid? projectId = null,
        int? year = null,
        int? month = null,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var targetYear = year ?? now.Year;
        var targetMonth = month ?? now.Month;

        var startDate = new DateOnly(targetYear, targetMonth, 1);
        var endDate = startDate.AddMonths(1).AddDays(-1);

        var retainerQuery = dbContext.ClientRetainers
            .AsNoTracking()
            .Include(r => r.Project)
            .AsQueryable();

        if (projectId.HasValue)
        {
            retainerQuery = retainerQuery.Where(r => r.ProjectId == projectId.Value);
        }

        var retainers = await retainerQuery
            .OrderBy(r => r.Project.Name)
            .ToListAsync(ct);

        if (retainers.Count == 0) return [];

        var projectIds = retainers.Select(r => r.ProjectId).ToList();

        var entries = await dbContext.TimeEntries
            .AsNoTracking()
            .Include(t => t.Employee)
            .Include(t => t.WorkItem)
            .Where(t => !t.IsRunning &&
                        t.IsBillable &&
                        t.LoggedDate >= startDate &&
                        t.LoggedDate <= endDate &&
                        projectIds.Contains(t.WorkItem.ProjectId))
            .ToListAsync(ct);

        var usageByProject = entries
            .GroupBy(t => t.WorkItem.ProjectId)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var hours = Math.Round((decimal)g.Sum(t => t.DurationMinutes) / 60m, 2, MidpointRounding.AwayFromZero);
                    var amount = Math.Round(
                        g.Sum(t => ((decimal)t.DurationMinutes / 60m) * t.Employee.HourlyBillableRate),
                        2,
                        MidpointRounding.AwayFromZero);
                    return (Hours: hours, Amount: amount);
                });

        return [.. retainers.Select(r =>
        {
            var usage = usageByProject.GetValueOrDefault(r.ProjectId, (Hours: 0m, Amount: 0m));
            return BuildStatusDto(r, targetYear, targetMonth, usage.Hours, usage.Amount);
        })];
    }

    public async Task<RetainerStatusDto> UpsertRetainerAsync(UpsertRetainerRequestDto request, CancellationToken ct = default)
    {
        if (request.MonthlyRetainerHours <= 0 && request.MonthlyBudgetAmount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Monthly retainer hours or budget amount must be greater than zero.");
        }

        var project = await dbContext.Projects.FirstOrDefaultAsync(p => p.Id == request.ProjectId, ct)
            ?? throw new KeyNotFoundException($"Project '{request.ProjectId}' not found.");

        var retainer = await dbContext.ClientRetainers
            .Include(r => r.Project)
            .FirstOrDefaultAsync(r => r.ProjectId == request.ProjectId, ct);

        if (retainer == null)
        {
            retainer = new ClientRetainer
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id,
                Project = project,
                CreatedAt = DateTimeOffset.UtcNow
            };
            dbContext.ClientRetainers.Add(retainer);
        }

        retainer.ClientName = request.ClientName.Trim();
        retainer.MonthlyRetainerHours = request.MonthlyRetainerHours;
        retainer.MonthlyBudgetAmount = request.MonthlyBudgetAmount;
        retainer.Currency = string.IsNullOrWhiteSpace(request.Currency) ? "USD" : request.Currency.Trim().ToUpperInvariant();
        retainer.WarningThresholdPercent = Math.Clamp(request.WarningThresholdPercent, 1m, 100m);
        retainer.IsActive = request.IsActive;
        retainer.NotifySlackTeams = request.NotifySlackTeams;
        retainer.NotifyEmail = request.NotifyEmail;
        retainer.UpdatedAt = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(ct);

        var list = await GetRetainersAsync(project.Id, ct: ct);
        return list[0];
    }

    public async Task DeleteRetainerAsync(Guid id, CancellationToken ct = default)
    {
        var retainer = await dbContext.ClientRetainers.FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new KeyNotFoundException($"Retainer '{id}' not found.");

        dbContext.ClientRetainers.Remove(retainer);
        await dbContext.SaveChangesAsync(ct);
    }

    public async Task<BudgetOverageEvaluationResultDto> EvaluateBudgetOveragesAsync(
        int? year = null,
        int? month = null,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var targetYear = year ?? now.Year;
        var targetMonth = month ?? now.Month;

        var statuses = await GetRetainersAsync(null, targetYear, targetMonth, ct);
        var active = statuses.Where(s => s.IsActive).ToList();
        var triggeredMessages = new List<string>();

        foreach (var status in active)
        {
            if (status.HealthStatus == RetainerHealthStatus.Healthy) continue;

            var alertLevel = status.HealthStatus == RetainerHealthStatus.Exceeded
                ? BudgetAlertLevel.Exceeded
                : BudgetAlertLevel.Warning;

            var alreadySent = await dbContext.BudgetOverageAlertLogs.AnyAsync(
                a => a.ProjectId == status.ProjectId &&
                     a.Year == targetYear &&
                     a.Month == targetMonth &&
                     a.AlertLevel == alertLevel,
                ct);

            if (alreadySent) continue;

            var maxUtil = Math.Max(status.BudgetUtilizationPercent, status.HoursUtilizationPercent);
            var isExceeded = alertLevel == BudgetAlertLevel.Exceeded;

            dbContext.BudgetOverageAlertLogs.Add(new BudgetOverageAlertLog
            {
                Id = Guid.NewGuid(),
                ProjectId = status.ProjectId,
                Year = targetYear,
                Month = targetMonth,
                AlertLevel = alertLevel,
                UtilizationPercent = maxUtil,
                LoggedAmount = status.LoggedBillableAmount,
                BudgetAmount = status.MonthlyBudgetAmount,
                SentAt = DateTimeOffset.UtcNow
            });

            await dbContext.SaveChangesAsync(ct);

            var msg = $"[{status.ProjectCode}] {status.ClientName} retainer {(isExceeded ? "EXCEEDED" : "WARNING")}: {maxUtil:0.0}% utilized ({status.LoggedBillableAmount:N2} / {status.MonthlyBudgetAmount:N2} {status.Currency}).";
            triggeredMessages.Add(msg);
            logger.LogWarning("Budget alarm triggered: {Message}", msg);

            if (status.NotifySlackTeams)
            {
                await chatWebhookService.SendBudgetOverageAlertAsync(
                    status.ProjectCode,
                    status.ProjectName,
                    status.ClientName,
                    maxUtil,
                    status.LoggedBillableAmount,
                    status.MonthlyBudgetAmount,
                    status.Currency,
                    isExceeded,
                    status.ProjectId,
                    ct);
            }

            if (status.NotifyEmail)
            {
                var subject = $"[CompanyCRM] {(isExceeded ? "Budget Exceeded" : "Budget Warning")}: {status.ProjectCode} ({status.ClientName})";
                dbContext.EmailNotificationLogs.Add(new EmailNotificationLog
                {
                    Id = Guid.NewGuid(),
                    RecipientEmail = "management@companycrm.local",
                    RecipientName = "Executive Management",
                    Subject = subject,
                    BodyHtml = $"<p>{msg}</p>",
                    TriggerEvent = isExceeded ? "BudgetOverageExceeded" : "BudgetOverageWarning",
                    SentAt = DateTimeOffset.UtcNow,
                    Success = true
                });
                await dbContext.SaveChangesAsync(ct);
            }
        }

        return new BudgetOverageEvaluationResultDto(
            EvaluatedRetainers: active.Count,
            AlertsTriggered: triggeredMessages.Count,
            TriggeredMessages: triggeredMessages);
    }

    private static RetainerStatusDto BuildStatusDto(
        ClientRetainer r,
        int year,
        int month,
        decimal loggedHours,
        decimal loggedAmount)
    {
        var hoursUtil = r.MonthlyRetainerHours > 0
            ? Math.Round((loggedHours / r.MonthlyRetainerHours) * 100m, 2, MidpointRounding.AwayFromZero)
            : 0m;

        var budgetUtil = r.MonthlyBudgetAmount > 0
            ? Math.Round((loggedAmount / r.MonthlyBudgetAmount) * 100m, 2, MidpointRounding.AwayFromZero)
            : 0m;

        var maxUtil = Math.Max(hoursUtil, budgetUtil);
        var health = maxUtil >= 100m
            ? RetainerHealthStatus.Exceeded
            : maxUtil >= r.WarningThresholdPercent
                ? RetainerHealthStatus.Warning
                : RetainerHealthStatus.Healthy;

        return new RetainerStatusDto(
            r.Id,
            r.ProjectId,
            r.Project.Name,
            r.Project.Code,
            r.ClientName,
            r.MonthlyRetainerHours,
            r.MonthlyBudgetAmount,
            r.Currency,
            r.WarningThresholdPercent,
            r.IsActive,
            r.NotifySlackTeams,
            r.NotifyEmail,
            year,
            month,
            loggedHours,
            loggedAmount,
            Math.Max(0m, r.MonthlyRetainerHours - loggedHours),
            Math.Max(0m, r.MonthlyBudgetAmount - loggedAmount),
            hoursUtil,
            budgetUtil,
            health,
            r.UpdatedAt);
    }
}
