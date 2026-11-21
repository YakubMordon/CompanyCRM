using System;
using System.Collections.Generic;
using CompanyCRM.Domain.Common;

namespace CompanyCRM.Domain.Entities;

public class DailyPlan : BaseEntity<Guid>
{
    public DailyPlan()
    {
        Id = Guid.NewGuid();
    }

    public Guid EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;

    public DateOnly PlanDate { get; set; }
    public DateTimeOffset SubmittedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool IsSubmittedOnTime { get; set; } = true;
    public string? Blockers { get; set; }
    public string? SummaryNotes { get; set; }

    public ICollection<DailyPlanItem> Items { get; set; } = new List<DailyPlanItem>();

    /// <summary>
    /// Evaluates whether this daily plan was submitted on time relative to the cutoff configuration
    /// in the target local timezone. Updates <see cref="SubmittedAt"/> (if provided) and <see cref="IsSubmittedOnTime"/>.
    /// </summary>
    /// <param name="config">The planning cutoff configuration defining cutoff time and timezone.</param>
    /// <param name="submittedAtUtc">Optional submission timestamp; if provided, updates <see cref="SubmittedAt"/>.</param>
    public void EvaluateSubmissionTiming(PlanningCutoffConfig config, DateTimeOffset? submittedAtUtc = null)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (submittedAtUtc.HasValue)
        {
            SubmittedAt = submittedAtUtc.Value;
        }

        TimeZoneInfo timeZone;
        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(config.TimeZoneId);
        }
        catch
        {
            timeZone = TimeZoneInfo.Utc;
        }

        var localSubmittedAt = TimeZoneInfo.ConvertTime(SubmittedAt, timeZone);
        var localTime = TimeOnly.FromTimeSpan(localSubmittedAt.TimeOfDay);
        var localDate = DateOnly.FromDateTime(localSubmittedAt.DateTime);

        if (PlanDate != default)
        {
            if (localDate < PlanDate)
            {
                IsSubmittedOnTime = true;
            }
            else if (localDate > PlanDate)
            {
                IsSubmittedOnTime = false;
            }
            else
            {
                IsSubmittedOnTime = localTime <= config.CutoffTimeLocal;
            }
        }
        else
        {
            IsSubmittedOnTime = localTime <= config.CutoffTimeLocal;
        }
    }

    /// <summary>
    /// Checks whether daily plan submission is permitted for the target PlanDate.
    /// Submissions are strictly limited until 23:59:59 on the plan date.
    /// </summary>
    public bool IsSubmissionPermitted(PlanningCutoffConfig config, DateTimeOffset? submittedAtUtc = null)
    {
        ArgumentNullException.ThrowIfNull(config);

        var timestamp = submittedAtUtc ?? SubmittedAt;

        TimeZoneInfo timeZone;
        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(config.TimeZoneId);
        }
        catch
        {
            timeZone = TimeZoneInfo.Utc;
        }

        var localSubmittedAt = TimeZoneInfo.ConvertTime(timestamp, timeZone);
        var localDate = DateOnly.FromDateTime(localSubmittedAt.DateTime);

        // Disallow submissions for dates strictly before the local submission date (past 23:59:59)
        return PlanDate == default || localDate <= PlanDate;
    }
}
