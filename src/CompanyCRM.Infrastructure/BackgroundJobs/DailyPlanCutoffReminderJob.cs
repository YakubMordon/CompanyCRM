using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using CompanyCRM.Application.DTOs;
using CompanyCRM.Application.Interfaces;
using CompanyCRM.Domain.Entities;
using CompanyCRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CompanyCRM.Infrastructure.BackgroundJobs;

public class DailyPlanCutoffReminderJob : IPlanningCutoffJob
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ScopeLocks = new();
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DailyPlanCutoffReminderJob> _logger;

    public DailyPlanCutoffReminderJob(
        IServiceScopeFactory scopeFactory,
        ILogger<DailyPlanCutoffReminderJob> logger,
        TimeProvider? timeProvider = null)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Task<CutoffExecutionSummaryDto> ExecuteCutoffCheckAsync(CancellationToken ct = default)
        => ExecuteCutoffCheckAsync(projectId: null, ct);

    public async Task<CutoffExecutionSummaryDto> ExecuteCutoffCheckAsync(Guid? projectId, CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var errors = new List<string>();
        var notifiedEmails = new List<string>();

        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
        var emailService = scope.ServiceProvider.GetRequiredService<IEmailNotificationService>();

        // 1. Retrieve Cutoff Config (project-specific override if requested, falling back to global ProjectId == null)
        PlanningCutoffConfig? config = null;
        if (projectId.HasValue)
        {
            config = await dbContext.PlanningCutoffConfigs
                .FirstOrDefaultAsync(c => c.ProjectId == projectId.Value, ct);
        }

        config ??= await dbContext.PlanningCutoffConfigs
            .FirstOrDefaultAsync(c => c.ProjectId == null, ct)
            ?? new PlanningCutoffConfig
            {
                CutoffTimeLocal = new TimeOnly(10, 0, 0),
                TimeZoneId = "UTC",
                IsReminderEnabled = true,
                ExcludedDaysOfWeekJson = "[0, 6]"
            };

        if (!config.IsReminderEnabled)
        {
            _logger.LogInformation("Daily plan cutoff reminders are disabled in configuration.");
            return new CutoffExecutionSummaryDto(
                Status: "Disabled",
                CheckedDate: DateOnly.FromDateTime(_timeProvider.GetUtcNow().DateTime),
                CutoffTime: config.CutoffTimeLocal,
                TimeZoneId: config.TimeZoneId,
                RemindersSent: 0,
                NotifiedEmployeeEmails: Array.Empty<string>(),
                ExecutionDurationMs: stopwatch.ElapsedMilliseconds,
                Errors: Array.Empty<string>());
        }

        // 2. Resolve Timezone
        var resolvedTz = ResolveTimeZone(config.TimeZoneId, _logger);
        var nowUtc = _timeProvider.GetUtcNow();
        var localNow = TimeZoneInfo.ConvertTime(nowUtc, resolvedTz);
        var localDate = DateOnly.FromDateTime(localNow.DateTime);
        var localTime = TimeOnly.FromTimeSpan(localNow.TimeOfDay);

        // 3. Excluded Days Check (e.g. weekends)
        var excludedDays = ParseExcludedDays(config.ExcludedDaysOfWeekJson);
        if (excludedDays.Contains((int)localNow.DayOfWeek))
        {
            _logger.LogInformation("Today ({DayOfWeek}, {Date}) is an excluded planning day. Skipping cutoff checks.",
                localNow.DayOfWeek, localDate);

            return new CutoffExecutionSummaryDto(
                Status: "ExcludedDay",
                CheckedDate: localDate,
                CutoffTime: config.CutoffTimeLocal,
                TimeZoneId: config.TimeZoneId,
                RemindersSent: 0,
                NotifiedEmployeeEmails: Array.Empty<string>(),
                ExecutionDurationMs: stopwatch.ElapsedMilliseconds,
                Errors: Array.Empty<string>());
        }

        // 4. Cutoff Deadline Check
        if (localTime < config.CutoffTimeLocal)
        {
            _logger.LogInformation("Current local time ({LocalTime}) is before cutoff deadline ({CutoffTime}). Skipping reminders.",
                localTime, config.CutoffTimeLocal);

            return new CutoffExecutionSummaryDto(
                Status: "BeforeCutoff",
                CheckedDate: localDate,
                CutoffTime: config.CutoffTimeLocal,
                TimeZoneId: config.TimeZoneId,
                RemindersSent: 0,
                NotifiedEmployeeEmails: Array.Empty<string>(),
                ExecutionDurationMs: stopwatch.ElapsedMilliseconds,
                Errors: Array.Empty<string>());
        }

        // 5. Employee Candidate Filtering & Idempotency Check (synchronized per scope to prevent concurrent race conditions)
        var lockKey = projectId?.ToString("N") ?? "GLOBAL";
        var scopeLock = ScopeLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        await scopeLock.WaitAsync(ct);
        try
        {
            List<Employee> allEmployees;
            if (projectId.HasValue)
            {
                var memberEmployeeIds = await dbContext.ProjectMembers
                    .Where(pm => pm.ProjectId == projectId.Value)
                    .Select(pm => pm.EmployeeId)
                    .Distinct()
                    .ToListAsync(ct);

                allEmployees = memberEmployeeIds.Count > 0
                    ? await dbContext.Employees.Where(e => memberEmployeeIds.Contains(e.Id)).ToListAsync(ct)
                    : new List<Employee>();
            }
            else
            {
                allEmployees = await dbContext.Employees.ToListAsync(ct);
            }

            if (allEmployees.Count == 0)
            {
                stopwatch.Stop();
                return new CutoffExecutionSummaryDto(
                    Status: "Completed",
                    CheckedDate: localDate,
                    CutoffTime: config.CutoffTimeLocal,
                    TimeZoneId: config.TimeZoneId,
                    RemindersSent: 0,
                    NotifiedEmployeeEmails: Array.Empty<string>(),
                    ExecutionDurationMs: stopwatch.ElapsedMilliseconds,
                    Errors: Array.Empty<string>());
            }

            var employeeIdsWithPlans = await dbContext.DailyPlans
                .Where(dp => dp.PlanDate == localDate)
                .Select(dp => dp.EmployeeId)
                .Distinct()
                .ToListAsync(ct);

            var existingLogsForScope = await dbContext.DailyPlanReminderLogs
                .Where(rl => rl.PlanDate == localDate && rl.ProjectId == projectId)
                .ToListAsync(ct);

            var employeeIdsWithSentReminders = existingLogsForScope
                .Where(rl => rl.EmailDeliveryStatus == "Sent" || rl.EmailDeliveryStatus == "Sending")
                .Select(rl => rl.EmployeeId)
                .ToHashSet();

            var failedLogsByEmployee = existingLogsForScope
                .Where(rl => rl.EmailDeliveryStatus == "Failed")
                .GroupBy(rl => rl.EmployeeId)
                .ToDictionary(g => g.Key, g => g.First());

            var candidateEmployees = allEmployees
                .Where(e => !employeeIdsWithPlans.Contains(e.Id) && !employeeIdsWithSentReminders.Contains(e.Id))
                .ToList();

            _logger.LogInformation(
                "Found {CandidateCount} employees missing daily plans for {Date} after cutoff {CutoffTime}.",
                candidateEmployees.Count, localDate, config.CutoffTimeLocal);

            // 6. Claim-then-send Reminder Emails & Save Logs
            foreach (var employee in candidateEmployees)
            {
                DailyPlanReminderLog claimLog;
                if (failedLogsByEmployee.TryGetValue(employee.Id, out var existingFailed))
                {
                    claimLog = existingFailed;
                    claimLog.EmailDeliveryStatus = "Sending";
                    claimLog.ErrorMessage = null;
                    claimLog.ReminderSentAt = _timeProvider.GetUtcNow();
                    claimLog.RecipientEmail = employee.Email;
                }
                else
                {
                    claimLog = new DailyPlanReminderLog
                    {
                        Id = Guid.NewGuid(),
                        EmployeeId = employee.Id,
                        ProjectId = projectId,
                        PlanDate = localDate,
                        ReminderSentAt = _timeProvider.GetUtcNow(),
                        RecipientEmail = employee.Email,
                        EmailDeliveryStatus = "Sending",
                        ErrorMessage = null
                    };
                    dbContext.DailyPlanReminderLogs.Add(claimLog);
                }

                try
                {
                    await dbContext.SaveChangesAsync(ct);
                }
                catch (DbUpdateException)
                {
                    // Another concurrent worker already claimed this (EmployeeId, PlanDate, ProjectId) slot
                    dbContext.Entry(claimLog).State = EntityState.Detached;
                    continue;
                }

                try
                {
                    await emailService.SendCutoffReminderEmailAsync(
                        employee.Email,
                        employee.FullName,
                        localDate,
                        config.CutoffTimeLocal,
                        ct);

                    claimLog.EmailDeliveryStatus = "Sent";
                    claimLog.ErrorMessage = null;
                    claimLog.ReminderSentAt = _timeProvider.GetUtcNow();
                    await dbContext.SaveChangesAsync(ct);

                    notifiedEmails.Add(employee.Email);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send cutoff reminder email to {Email}", employee.Email);
                    errors.Add($"Failed to send email to {employee.Email}: {ex.Message}");

                    claimLog.EmailDeliveryStatus = "Failed";
                    claimLog.ErrorMessage = ex.Message.Length <= 2000 ? ex.Message : ex.Message[..2000];
                    claimLog.ReminderSentAt = _timeProvider.GetUtcNow();
                    await dbContext.SaveChangesAsync(ct);
                }
            }
        }
        finally
        {
            scopeLock.Release();
        }

        stopwatch.Stop();
        _logger.LogInformation("Daily plan cutoff check finished. Sent {SentCount} reminders in {Duration}ms.",
            notifiedEmails.Count, stopwatch.ElapsedMilliseconds);

        return new CutoffExecutionSummaryDto(
            Status: "Completed",
            CheckedDate: localDate,
            CutoffTime: config.CutoffTimeLocal,
            TimeZoneId: config.TimeZoneId,
            RemindersSent: notifiedEmails.Count,
            NotifiedEmployeeEmails: notifiedEmails,
            ExecutionDurationMs: stopwatch.ElapsedMilliseconds,
            Errors: errors);
    }

    public static bool TryResolveTimeZone(string? timeZoneId, out TimeZoneInfo timeZone)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            timeZone = TimeZoneInfo.Utc;
            return true;
        }

        var trimmed = timeZoneId.Trim();
        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(trimmed);
            return true;
        }
        catch
        {
            var alias = trimmed switch
            {
                "Eastern Standard Time" => "America/New_York",
                "America/New_York" => "Eastern Standard Time",
                "Central Standard Time" => "America/Chicago",
                "America/Chicago" => "Central Standard Time",
                "Pacific Standard Time" => "America/Los_Angeles",
                "America/Los_Angeles" => "Pacific Standard Time",
                "GMT Standard Time" => "Europe/London",
                "Europe/London" => "GMT Standard Time",
                "W. Europe Standard Time" => "Europe/Berlin",
                "Europe/Berlin" => "W. Europe Standard Time",
                _ => null
            };

            if (alias != null)
            {
                try
                {
                    timeZone = TimeZoneInfo.FindSystemTimeZoneById(alias);
                    return true;
                }
                catch
                {
                    // Unrecognized
                }
            }

            timeZone = TimeZoneInfo.Utc;
            return false;
        }
    }

    private static TimeZoneInfo ResolveTimeZone(string timeZoneId, ILogger logger)
    {
        if (TryResolveTimeZone(timeZoneId, out var resolved))
        {
            return resolved;
        }

        logger.LogWarning("Timezone '{TimeZoneId}' not recognized on current host. Falling back to UTC.", timeZoneId);
        return TimeZoneInfo.Utc;
    }

    public static HashSet<int> ParseExcludedDays(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new HashSet<int> { 0, 6 };
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return new HashSet<int> { 0, 6 };
            }

            var result = new HashSet<int>();
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                if (element.ValueKind == JsonValueKind.Number &&
                    element.TryGetInt32(out var num) &&
                    num is >= 0 and <= 6)
                {
                    result.Add(num);
                }
                else if (element.ValueKind == JsonValueKind.String)
                {
                    var raw = element.GetString()?.Trim();
                    if (string.IsNullOrEmpty(raw)) continue;

                    if (int.TryParse(raw, out var parsedNum))
                    {
                        if (parsedNum is >= 0 and <= 6)
                        {
                            result.Add(parsedNum);
                        }
                    }
                    else if (Enum.TryParse<DayOfWeek>(raw, ignoreCase: true, out var dow) &&
                             Enum.IsDefined(typeof(DayOfWeek), dow))
                    {
                        result.Add((int)dow);
                    }
                }
            }

            return result;
        }
        catch
        {
            return new HashSet<int> { 0, 6 };
        }
    }
}
