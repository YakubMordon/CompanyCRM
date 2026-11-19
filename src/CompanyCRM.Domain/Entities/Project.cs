using CompanyCRM.Domain.Common;
using CompanyCRM.Domain.Enums;

namespace CompanyCRM.Domain.Entities;

public class Project : BaseEntity<Guid>
{
    public Project()
    {
        Id = Guid.NewGuid();
    }

    public string Code { get; set; } = string.Empty; // e.g., "CRM", "PRJ"
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;

    // External Issue Tracker Configuration
    public ExternalSourceType ExternalSource { get; set; } = ExternalSourceType.None;
    public string? ExternalOrgUrl { get; set; }
    public string? ExternalProjectName { get; set; }
    public string? ExternalPat { get; set; }
    public bool IsSyncEnabled { get; set; } = false;
    public bool IsSimulatedMode { get; set; } = true;
    public DateTimeOffset? LastSyncRunAt { get; set; }

    // Navigation Properties
    public ICollection<ProjectMember> Members { get; set; } = new List<ProjectMember>();
    public ICollection<WorkItem> WorkItems { get; set; } = new List<WorkItem>();
    public ICollection<SyncConflict> SyncConflicts { get; set; } = new List<SyncConflict>();
    public ICollection<WeeklyPlan> WeeklyPlans { get; set; } = new List<WeeklyPlan>();
    public ICollection<PlanningCutoffConfig> CutoffConfigs { get; set; } = new List<PlanningCutoffConfig>();
}
