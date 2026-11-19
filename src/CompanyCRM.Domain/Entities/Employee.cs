using CompanyCRM.Domain.Common;
using CompanyCRM.Domain.Enums;

namespace CompanyCRM.Domain.Entities;

public class Employee : BaseEntity<Guid>
{
    public Employee()
    {
        Id = Guid.NewGuid();
    }

    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public string Department { get; set; } = string.Empty;
    public string JobTitle { get; set; } = string.Empty;
    public SeniorityRank SeniorityRank { get; set; } = SeniorityRank.Mid;
    public AppRole Role { get; set; } = AppRole.Employee;
    public decimal WeeklyCapacityHours { get; set; } = 40.0m;
    public string? Bio { get; set; }
    public string? LayoutPreferencesJson { get; set; }

    // Restricted Management Metadata (Exposed Strictly to HigherManagement)
    public decimal AnnualBaseSalary { get; set; } = 0.0m;
    public decimal HourlyCostRate { get; set; } = 0.0m;
    public decimal HourlyBillableRate { get; set; } = 0.0m;
    public decimal TargetUtilizationPercent { get; set; } = 80.0m;

    // Navigation Collections
    public ICollection<ProjectMember> ProjectMemberships { get; set; } = new List<ProjectMember>();
    public ICollection<WorkItem> CreatedWorkItems { get; set; } = new List<WorkItem>();
    public ICollection<WorkItem> AssignedWorkItems { get; set; } = new List<WorkItem>();
    public ICollection<WorkItem> ReviewedWorkItems { get; set; } = new List<WorkItem>();
    public ICollection<TimeEntry> TimeEntries { get; set; } = new List<TimeEntry>();
    public ICollection<EmployeeSkill> Skills { get; set; } = new List<EmployeeSkill>();
    public ICollection<WeeklyPlan> WeeklyPlans { get; set; } = new List<WeeklyPlan>();
    public ICollection<DailyPlan> DailyPlans { get; set; } = new List<DailyPlan>();
    public ICollection<DailyPlanReminderLog> ReminderLogs { get; set; } = new List<DailyPlanReminderLog>();
    public ICollection<WorkItemHistory> HistoryEntries { get; set; } = new List<WorkItemHistory>();
}
