using CompanyCRM.Domain.Common;

namespace CompanyCRM.Domain.Entities;

public class TimeEntry : BaseEntity<Guid>
{
    public TimeEntry()
    {
        Id = Guid.NewGuid();
    }

    public Guid WorkItemId { get; set; }
    public WorkItem WorkItem { get; set; } = null!;

    public Guid EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;

    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? EndedAt { get; set; }
    public int DurationMinutes { get; set; } = 0;
    public DateOnly LoggedDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);
    public bool IsRunning { get; set; } = false;
    public bool IsBillable { get; set; } = true;
    public string? Notes { get; set; }

    public void StopTimer(DateTimeOffset endedAt, string? notes = null)
    {
        EndedAt = endedAt;
        IsRunning = false;
        var elapsed = (endedAt - StartedAt).TotalMinutes;
        DurationMinutes = Math.Max(1, (int)Math.Round(elapsed));
        if (!string.IsNullOrWhiteSpace(notes))
        {
            Notes = notes.Trim();
        }
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
