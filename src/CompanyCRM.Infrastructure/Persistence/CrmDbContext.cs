using CompanyCRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CompanyCRM.Infrastructure.Persistence;

public class CrmDbContext : DbContext
{
    public CrmDbContext(DbContextOptions<CrmDbContext> options)
        : base(options)
    {
    }

    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ProjectMember> ProjectMembers => Set<ProjectMember>();
    public DbSet<WorkItem> WorkItems => Set<WorkItem>();
    public DbSet<WorkItemHistory> WorkItemHistories => Set<WorkItemHistory>();
    public DbSet<TimeEntry> TimeEntries => Set<TimeEntry>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<EmployeeSkill> EmployeeSkills => Set<EmployeeSkill>();
    public DbSet<WeeklyPlan> WeeklyPlans => Set<WeeklyPlan>();
    public DbSet<DailyPlan> DailyPlans => Set<DailyPlan>();
    public DbSet<DailyPlanItem> DailyPlanItems => Set<DailyPlanItem>();
    public DbSet<PlanningCutoffConfig> PlanningCutoffConfigs => Set<PlanningCutoffConfig>();
    public DbSet<DailyPlanReminderLog> DailyPlanReminderLogs => Set<DailyPlanReminderLog>();
    public DbSet<SyncConflict> SyncConflicts => Set<SyncConflict>();
    public DbSet<EmailNotificationLog> EmailNotificationLogs => Set<EmailNotificationLog>();
    public DbSet<ChatWebhookConfig> ChatWebhookConfigs => Set<ChatWebhookConfig>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CrmDbContext).Assembly);
    }
}
