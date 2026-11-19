using CompanyCRM.Domain.Entities;
using CompanyCRM.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CompanyCRM.Infrastructure.Persistence;

public class DatabaseSeeder
{
    private readonly CrmDbContext _context;
    private readonly ILogger<DatabaseSeeder> _logger;

    public DatabaseSeeder(CrmDbContext context, ILogger<DatabaseSeeder> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken ct = default)
    {
        if (await _context.Employees.AnyAsync(ct))
        {
            _logger.LogInformation("Database already seeded. Skipping initial seeding.");
            return;
        }

        _logger.LogInformation("Beginning database seeding with demo data...");

        // 1. Employees (6 Real-World Personas)
        var eleanor = new Employee
        {
            FullName = "Eleanor Vance",
            Email = "eleanor.vance@companycrm.local",
            AvatarUrl = "https://images.unsplash.com/photo-1573496359142-b8d87734a5a2?w=150",
            Department = "Executive",
            JobTitle = "Chief Operating Officer",
            SeniorityRank = SeniorityRank.Principal,
            Role = AppRole.HigherManagement,
            WeeklyCapacityHours = 40.0m,
            Bio = "Executive leader overseeing global service operations, revenue management, and cross-functional team delivery.",
            LayoutPreferencesJson = "{\"theme\":\"dark\",\"density\":\"compact\"}",
            AnnualBaseSalary = 185000.00m,
            HourlyCostRate = 88.94m,
            HourlyBillableRate = 220.00m,
            TargetUtilizationPercent = 60.0m
        };

        var marcus = new Employee
        {
            FullName = "Marcus Brody",
            Email = "marcus.brody@companycrm.local",
            AvatarUrl = "https://images.unsplash.com/photo-1500648767791-00dcc994a43e?w=150",
            Department = "Engineering",
            JobTitle = "VP of Engineering",
            SeniorityRank = SeniorityRank.Lead,
            Role = AppRole.HigherManagement,
            WeeklyCapacityHours = 40.0m,
            Bio = "Engineering leader focused on cloud-native scalable systems, DevOps automation, and developer productivity.",
            LayoutPreferencesJson = "{\"theme\":\"dark\",\"density\":\"standard\"}",
            AnnualBaseSalary = 165000.00m,
            HourlyCostRate = 79.33m,
            HourlyBillableRate = 195.00m,
            TargetUtilizationPercent = 70.0m
        };

        var sarah = new Employee
        {
            FullName = "Sarah Jenkins",
            Email = "sarah.jenkins@companycrm.local",
            AvatarUrl = "https://images.unsplash.com/photo-1580489944761-15a19d654956?w=150",
            Department = "Engineering",
            JobTitle = "Engineering Manager & Lead Architect",
            SeniorityRank = SeniorityRank.Lead,
            Role = AppRole.TeamLead,
            WeeklyCapacityHours = 40.0m,
            Bio = "Tech lead and software architect specializing in distributed architectures, EF Core optimization, and code review governance.",
            LayoutPreferencesJson = "{\"theme\":\"system\",\"density\":\"standard\"}",
            AnnualBaseSalary = 140000.00m,
            HourlyCostRate = 67.31m,
            HourlyBillableRate = 175.00m,
            TargetUtilizationPercent = 80.0m
        };

        var david = new Employee
        {
            FullName = "David Chen",
            Email = "david.chen@companycrm.local",
            AvatarUrl = "https://images.unsplash.com/photo-1507003211169-0a1dd7228f2d?w=150",
            Department = "Quality Assurance",
            JobTitle = "QA & Delivery Lead",
            SeniorityRank = SeniorityRank.Senior,
            Role = AppRole.TeamLead,
            WeeklyCapacityHours = 40.0m,
            Bio = "Quality assurance manager driving end-to-end automation, regression gates, and release compliance.",
            LayoutPreferencesJson = "{\"theme\":\"light\",\"density\":\"standard\"}",
            AnnualBaseSalary = 125000.00m,
            HourlyCostRate = 60.10m,
            HourlyBillableRate = 160.00m,
            TargetUtilizationPercent = 85.0m
        };

        var alex = new Employee
        {
            FullName = "Alex Rivera",
            Email = "alex.rivera@companycrm.local",
            AvatarUrl = "https://images.unsplash.com/photo-1534528741775-53994a69daeb?w=150",
            Department = "Engineering",
            JobTitle = "Senior Full Stack Developer",
            SeniorityRank = SeniorityRank.Mid,
            Role = AppRole.Employee,
            WeeklyCapacityHours = 40.0m,
            Bio = "Full stack engineer delivering high-throughput REST APIs, background job workers, and real-time SignalR integrations.",
            LayoutPreferencesJson = "{\"theme\":\"dark\",\"density\":\"compact\"}",
            AnnualBaseSalary = 95000.00m,
            HourlyCostRate = 45.67m,
            HourlyBillableRate = 135.00m,
            TargetUtilizationPercent = 85.0m
        };

        var maya = new Employee
        {
            FullName = "Maya Patel",
            Email = "maya.patel@companycrm.local",
            AvatarUrl = "https://images.unsplash.com/photo-1517841905240-472988babdf9?w=150",
            Department = "Frontend & Design",
            JobTitle = "Frontend UI/UX Specialist",
            SeniorityRank = SeniorityRank.Junior,
            Role = AppRole.Employee,
            WeeklyCapacityHours = 40.0m,
            Bio = "Frontend developer specializing in modern React 19, Material UI components, drag-and-drop workflows, and responsive web apps.",
            LayoutPreferencesJson = "{\"theme\":\"light\",\"density\":\"comfortable\"}",
            AnnualBaseSalary = 75000.00m,
            HourlyCostRate = 36.06m,
            HourlyBillableRate = 115.00m,
            TargetUtilizationPercent = 90.0m
        };

        _context.Employees.AddRange(eleanor, marcus, sarah, david, alex, maya);

        // Skills Matrix
        _context.EmployeeSkills.AddRange(
            new EmployeeSkill { EmployeeId = eleanor.Id, SkillName = "System Architecture", Category = "Architecture", ProficiencyLevel = 5, YearsOfExperience = 12.0m },
            new EmployeeSkill { EmployeeId = eleanor.Id, SkillName = "Agile Coaching", Category = "Management", ProficiencyLevel = 5, YearsOfExperience = 10.0m },
            new EmployeeSkill { EmployeeId = marcus.Id, SkillName = "Cloud Architecture", Category = "DevOps", ProficiencyLevel = 5, YearsOfExperience = 11.0m },
            new EmployeeSkill { EmployeeId = marcus.Id, SkillName = "ASP.NET Core", Category = "Backend", ProficiencyLevel = 5, YearsOfExperience = 9.0m },
            new EmployeeSkill { EmployeeId = sarah.Id, SkillName = "C#", Category = "Backend", ProficiencyLevel = 5, YearsOfExperience = 8.0m },
            new EmployeeSkill { EmployeeId = sarah.Id, SkillName = "EF Core", Category = "Database", ProficiencyLevel = 5, YearsOfExperience = 7.0m },
            new EmployeeSkill { EmployeeId = david.Id, SkillName = "Test Automation", Category = "QA", ProficiencyLevel = 5, YearsOfExperience = 7.0m },
            new EmployeeSkill { EmployeeId = david.Id, SkillName = "Playwright", Category = "QA", ProficiencyLevel = 5, YearsOfExperience = 4.0m },
            new EmployeeSkill { EmployeeId = alex.Id, SkillName = "C#", Category = "Backend", ProficiencyLevel = 4, YearsOfExperience = 5.0m },
            new EmployeeSkill { EmployeeId = alex.Id, SkillName = "PostgreSQL", Category = "Database", ProficiencyLevel = 4, YearsOfExperience = 4.0m },
            new EmployeeSkill { EmployeeId = alex.Id, SkillName = "React 19", Category = "Frontend", ProficiencyLevel = 3, YearsOfExperience = 2.0m },
            new EmployeeSkill { EmployeeId = maya.Id, SkillName = "React 19", Category = "Frontend", ProficiencyLevel = 4, YearsOfExperience = 3.0m },
            new EmployeeSkill { EmployeeId = maya.Id, SkillName = "Material UI", Category = "Frontend", ProficiencyLevel = 5, YearsOfExperience = 3.0m },
            new EmployeeSkill { EmployeeId = maya.Id, SkillName = "TypeScript", Category = "Frontend", ProficiencyLevel = 4, YearsOfExperience = 3.0m }
        );

        // 2. Projects (3 Isolated Workspaces)
        var projectCrm = new Project
        {
            Code = "CRM",
            Name = "CompanyCRM Enterprise Platform",
            Description = "Full-stack enterprise CRM, project workspace, and time analytics application.",
            IsActive = true,
            ExternalSource = ExternalSourceType.AzureDevOps,
            ExternalOrgUrl = "https://dev.azure.com/companycrm",
            ExternalProjectName = "CompanyCRM-Cloud",
            IsSyncEnabled = true,
            IsSimulatedMode = true,
            LastSyncRunAt = DateTimeOffset.UtcNow.AddMinutes(-10)
        };

        var projectFin = new Project
        {
            Code = "FIN",
            Name = "Financial Billing & Margin Analytics",
            Description = "Internal client billing engine, payroll integration, and margin forecasting platform.",
            IsActive = true,
            ExternalSource = ExternalSourceType.None,
            IsSyncEnabled = false,
            IsSimulatedMode = false
        };

        var projectMob = new Project
        {
            Code = "MOB",
            Name = "Field Agent Operations Mobile App",
            Description = "Offline-capable mobile client for field service technicians.",
            IsActive = true,
            ExternalSource = ExternalSourceType.AzureDevOps,
            ExternalOrgUrl = "https://dev.azure.com/companycrm",
            ExternalProjectName = "FieldOps-Mobile",
            IsSyncEnabled = true,
            IsSimulatedMode = true
        };

        _context.Projects.AddRange(projectCrm, projectFin, projectMob);

        // Project Memberships
        _context.ProjectMembers.AddRange(
            new ProjectMember { ProjectId = projectCrm.Id, EmployeeId = eleanor.Id, Role = ProjectRole.Manager },
            new ProjectMember { ProjectId = projectCrm.Id, EmployeeId = marcus.Id, Role = ProjectRole.Manager },
            new ProjectMember { ProjectId = projectCrm.Id, EmployeeId = sarah.Id, Role = ProjectRole.Lead },
            new ProjectMember { ProjectId = projectCrm.Id, EmployeeId = david.Id, Role = ProjectRole.Lead },
            new ProjectMember { ProjectId = projectCrm.Id, EmployeeId = alex.Id, Role = ProjectRole.Member },
            new ProjectMember { ProjectId = projectCrm.Id, EmployeeId = maya.Id, Role = ProjectRole.Member },

            new ProjectMember { ProjectId = projectFin.Id, EmployeeId = eleanor.Id, Role = ProjectRole.Manager },
            new ProjectMember { ProjectId = projectFin.Id, EmployeeId = marcus.Id, Role = ProjectRole.Lead },
            new ProjectMember { ProjectId = projectFin.Id, EmployeeId = alex.Id, Role = ProjectRole.Member },

            new ProjectMember { ProjectId = projectMob.Id, EmployeeId = marcus.Id, Role = ProjectRole.Manager },
            new ProjectMember { ProjectId = projectMob.Id, EmployeeId = sarah.Id, Role = ProjectRole.Lead },
            new ProjectMember { ProjectId = projectMob.Id, EmployeeId = maya.Id, Role = ProjectRole.Member }
        );

        // 3. Work Items across all 9 statuses
        var crm101 = new WorkItem
        {
            ProjectId = projectCrm.Id,
            CreatedById = sarah.Id,
            AssigneeId = alex.Id,
            ReviewerId = sarah.Id,
            Code = "CRM-101",
            Title = "Architect Dual-Mode Database Persistence Layer",
            Description = "Implement CrmDbContext with fluent configurations, dual-mode Postgres/SQLite probe, and automatic fallback.",
            Type = WorkItemType.Story,
            Status = WorkItemStatus.Done,
            Priority = WorkItemPriority.High,
            EstimatedHours = 16.0m,
            StoryPoints = 5,
            SprintName = "Sprint 1",
            ReviewStatus = ReviewStatus.Approved,
            ReviewedAt = DateTimeOffset.UtcNow.AddDays(-3),
            ExternalSource = ExternalSourceType.AzureDevOps,
            ExternalId = "1001",
            ExternalUrl = "https://dev.azure.com/companycrm/CompanyCRM-Cloud/_workitems/edit/1001",
            LastSyncedAt = DateTimeOffset.UtcNow.AddDays(-2),
            LastSyncOrigin = SyncOrigin.LocalCrm
        };

        var crm102 = new WorkItem
        {
            ProjectId = projectCrm.Id,
            CreatedById = sarah.Id,
            AssigneeId = alex.Id,
            ReviewerId = sarah.Id,
            Code = "CRM-102",
            Title = "Implement Reviewer Approval State Machine",
            Description = "Validate transitions for Pending, ClarificationNeeded, Approved, and Denied states.",
            Type = WorkItemType.Task,
            Status = WorkItemStatus.Done,
            Priority = WorkItemPriority.High,
            EstimatedHours = 8.0m,
            StoryPoints = 3,
            SprintName = "Sprint 1",
            ReviewStatus = ReviewStatus.Approved,
            ReviewedAt = DateTimeOffset.UtcNow.AddDays(-2),
            ExternalSource = ExternalSourceType.AzureDevOps,
            ExternalId = "1002",
            ExternalUrl = "https://dev.azure.com/companycrm/CompanyCRM-Cloud/_workitems/edit/1002",
            LastSyncedAt = DateTimeOffset.UtcNow.AddDays(-1),
            LastSyncOrigin = SyncOrigin.LocalCrm
        };

        var crm103 = new WorkItem
        {
            ProjectId = projectCrm.Id,
            CreatedById = sarah.Id,
            AssigneeId = alex.Id,
            ReviewerId = david.Id,
            Code = "CRM-103",
            Title = "Real-Time Work Item Updates via SignalR",
            Description = "Broadcast work item changes and timer state to /hubs/crm.",
            Type = WorkItemType.Story,
            Status = WorkItemStatus.InVerify,
            Priority = WorkItemPriority.Medium,
            EstimatedHours = 12.0m,
            StoryPoints = 5,
            SprintName = "Sprint 2",
            ReviewStatus = ReviewStatus.Approved,
            ReviewedAt = DateTimeOffset.UtcNow.AddHours(-12)
        };

        var crm104 = new WorkItem
        {
            ProjectId = projectCrm.Id,
            CreatedById = sarah.Id,
            AssigneeId = maya.Id,
            ReviewerId = sarah.Id,
            Code = "CRM-104",
            Title = "Interactive Kanban Board Drag-and-Drop Column Transitions",
            Description = "Support drag-and-drop column transitions across Backlog, Todo, InProgress, InVerify, and Done.",
            Type = WorkItemType.Task,
            Status = WorkItemStatus.InProgress,
            Priority = WorkItemPriority.Critical,
            EstimatedHours = 20.0m,
            StoryPoints = 8,
            SprintName = "Sprint 2",
            ReviewStatus = ReviewStatus.Approved,
            ReviewedAt = DateTimeOffset.UtcNow.AddHours(-24)
        };

        var crm105 = new WorkItem
        {
            ProjectId = projectCrm.Id,
            CreatedById = david.Id,
            AssigneeId = maya.Id,
            ReviewerId = david.Id,
            Code = "CRM-105",
            Title = "Timer Drawer Fails to Re-render on Stop Event",
            Description = "Stopping timer leaves drawer in running state until browser refresh.",
            Type = WorkItemType.Bug,
            Status = WorkItemStatus.Todo,
            Priority = WorkItemPriority.High,
            EstimatedHours = 4.0m,
            StoryPoints = 2,
            SprintName = "Sprint 2",
            ReviewStatus = ReviewStatus.Approved,
            ReviewedAt = DateTimeOffset.UtcNow.AddHours(-6)
        };

        var crm106 = new WorkItem
        {
            ProjectId = projectCrm.Id,
            CreatedById = sarah.Id,
            AssigneeId = maya.Id,
            Code = "CRM-106",
            Title = "Employee Directory and Skills Matrix Radar Chart",
            Description = "Display proficiency ratings across all employees in skills matrix.",
            Type = WorkItemType.Story,
            Status = WorkItemStatus.Backlog,
            Priority = WorkItemPriority.Low,
            EstimatedHours = 10.0m,
            StoryPoints = 3,
            ReviewStatus = ReviewStatus.NotRequired
        };

        var crm107 = new WorkItem
        {
            ProjectId = projectCrm.Id,
            CreatedById = alex.Id,
            AssigneeId = alex.Id,
            ReviewerId = sarah.Id,
            Code = "CRM-107",
            Title = "Automated Daily Cutoff Check Background Worker",
            Description = "Implement Hangfire recurring job that scans for missing daily plans after local deadline.",
            Type = WorkItemType.Story,
            Status = WorkItemStatus.PendingReview,
            Priority = WorkItemPriority.High,
            EstimatedHours = 14.0m,
            StoryPoints = 5,
            ReviewStatus = ReviewStatus.Pending
        };

        var crm108 = new WorkItem
        {
            ProjectId = projectCrm.Id,
            CreatedById = alex.Id,
            AssigneeId = alex.Id,
            ReviewerId = david.Id,
            Code = "CRM-108",
            Title = "PostgreSQL DateOnly Conversion Offset in Hangfire Cron",
            Description = "Npgsql throws InvalidCastException when passing non-UTC DateTimeOffset to Hangfire.",
            Type = WorkItemType.Bug,
            Status = WorkItemStatus.ClarificationRequested,
            Priority = WorkItemPriority.Medium,
            EstimatedHours = 6.0m,
            StoryPoints = 3,
            ReviewStatus = ReviewStatus.ClarificationNeeded,
            ReviewLatestComment = "Please attach the stack trace from the Linux container environment and specify if Npgsql 9.0 is throwing InvalidCastException.",
            ReviewedAt = DateTimeOffset.UtcNow.AddHours(-3)
        };

        var crm109 = new WorkItem
        {
            ProjectId = projectCrm.Id,
            CreatedById = alex.Id,
            AssigneeId = alex.Id,
            ReviewerId = marcus.Id,
            Code = "CRM-109",
            Title = "Bypass Authorization Header in Development Proxy",
            Description = "Add dev proxy middleware that injects HigherManagement claims without authentication.",
            Type = WorkItemType.Task,
            Status = WorkItemStatus.Denied,
            Priority = WorkItemPriority.Low,
            EstimatedHours = 3.0m,
            StoryPoints = 1,
            ReviewStatus = ReviewStatus.Denied,
            ReviewLatestComment = "Denied: Bypassing auth headers in dev violates security policy R5. Use Persona Switcher instead.",
            ReviewedAt = DateTimeOffset.UtcNow.AddHours(-5)
        };

        var crm110 = new WorkItem
        {
            ProjectId = projectCrm.Id,
            CreatedById = eleanor.Id,
            Code = "CRM-110",
            Title = "Restricted Executive Compensation and Margin Analytics Grid",
            Description = "Design management data grid showing salary, hourly rates, and target utilization.",
            Type = WorkItemType.Story,
            Status = WorkItemStatus.Draft,
            Priority = WorkItemPriority.High,
            EstimatedHours = 24.0m,
            StoryPoints = 8,
            ReviewStatus = ReviewStatus.NotRequired
        };

        var fin201 = new WorkItem
        {
            ProjectId = projectFin.Id,
            CreatedById = marcus.Id,
            AssigneeId = alex.Id,
            ReviewerId = marcus.Id,
            Code = "FIN-201",
            Title = "Client Invoice Generator and Profitability Calculation",
            Description = "Calculate gross profit and margins based on employee hourly cost rate and billable rate.",
            Type = WorkItemType.Story,
            Status = WorkItemStatus.InProgress,
            Priority = WorkItemPriority.Critical,
            EstimatedHours = 18.0m,
            StoryPoints = 5,
            ReviewStatus = ReviewStatus.Approved,
            ReviewedAt = DateTimeOffset.UtcNow.AddDays(-1)
        };

        var fin202 = new WorkItem
        {
            ProjectId = projectFin.Id,
            CreatedById = marcus.Id,
            AssigneeId = alex.Id,
            ReviewerId = marcus.Id,
            Code = "FIN-202",
            Title = "Hourly Billable Rate Precision Rounding Discrepancy",
            Description = "Fix 2-decimal roundoff in profitability summary.",
            Type = WorkItemType.Bug,
            Status = WorkItemStatus.Done,
            Priority = WorkItemPriority.Medium,
            EstimatedHours = 5.0m,
            StoryPoints = 2,
            ReviewStatus = ReviewStatus.Approved,
            ReviewedAt = DateTimeOffset.UtcNow.AddDays(-2)
        };

        var mob301 = new WorkItem
        {
            ProjectId = projectMob.Id,
            CreatedById = sarah.Id,
            Code = "MOB-301",
            Title = "Offline Work Item Sync Queue for Mobile Workers",
            Description = "Cache local SQLite state on mobile device and synchronize when internet returns.",
            Type = WorkItemType.Story,
            Status = WorkItemStatus.Backlog,
            Priority = WorkItemPriority.Medium,
            EstimatedHours = 32.0m,
            StoryPoints = 13,
            ReviewStatus = ReviewStatus.NotRequired
        };

        var mob302 = new WorkItem
        {
            ProjectId = projectMob.Id,
            CreatedById = sarah.Id,
            AssigneeId = maya.Id,
            ReviewerId = david.Id,
            Code = "MOB-302",
            Title = "Push Notification Token Expiry on Android 14",
            Description = "Renew FCM tokens periodically to prevent lost cutoff reminders.",
            Type = WorkItemType.Bug,
            Status = WorkItemStatus.Todo,
            Priority = WorkItemPriority.High,
            EstimatedHours = 8.0m,
            StoryPoints = 3,
            ReviewStatus = ReviewStatus.Approved,
            ReviewedAt = DateTimeOffset.UtcNow.AddHours(-1)
        };

        _context.WorkItems.AddRange(
            crm101, crm102, crm103, crm104, crm105,
            crm106, crm107, crm108, crm109, crm110,
            fin201, fin202, mob301, mob302);

        // 4. Audit Histories
        _context.WorkItemHistories.AddRange(
            new WorkItemHistory
            {
                WorkItemId = crm101.Id,
                ChangedById = sarah.Id,
                Action = "Created",
                FieldName = "Status",
                OldValue = null,
                NewValue = "Backlog",
                Note = "Work item initialized",
                ChangedAt = DateTimeOffset.UtcNow.AddDays(-4)
            },
            new WorkItemHistory
            {
                WorkItemId = crm101.Id,
                ChangedById = sarah.Id,
                Action = "ReviewApproved",
                FieldName = "ReviewStatus",
                OldValue = "Pending",
                NewValue = "Approved",
                Note = "Technical specification approved for implementation",
                ChangedAt = DateTimeOffset.UtcNow.AddDays(-3)
            },
            new WorkItemHistory
            {
                WorkItemId = crm108.Id,
                ChangedById = david.Id,
                Action = "ReviewClarificationRequested",
                FieldName = "ReviewStatus",
                OldValue = "Pending",
                NewValue = "ClarificationNeeded",
                Note = crm108.ReviewLatestComment,
                ChangedAt = DateTimeOffset.UtcNow.AddHours(-3)
            },
            new WorkItemHistory
            {
                WorkItemId = crm109.Id,
                ChangedById = marcus.Id,
                Action = "ReviewDenied",
                FieldName = "ReviewStatus",
                OldValue = "Pending",
                NewValue = "Denied",
                Note = crm109.ReviewLatestComment,
                ChangedAt = DateTimeOffset.UtcNow.AddHours(-5)
            }
        );

        // 5. Time Entries (Live Timers + Completed Entries)
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var yesterday = today.AddDays(-1);

        _context.TimeEntries.AddRange(
            // Maya running timer on CRM-104 (started 45m ago)
            new TimeEntry
            {
                WorkItemId = crm104.Id,
                EmployeeId = maya.Id,
                StartedAt = DateTimeOffset.UtcNow.AddMinutes(-45),
                EndedAt = null,
                DurationMinutes = 0,
                LoggedDate = today,
                IsRunning = true,
                IsBillable = true,
                Notes = "Working on drag-and-drop animation and state transitions"
            },
            // Alex running timer on FIN-201 (started 25m ago)
            new TimeEntry
            {
                WorkItemId = fin201.Id,
                EmployeeId = alex.Id,
                StartedAt = DateTimeOffset.UtcNow.AddMinutes(-25),
                EndedAt = null,
                DurationMinutes = 0,
                LoggedDate = today,
                IsRunning = true,
                IsBillable = true,
                Notes = "Writing margin and gross profit formulas"
            },
            // Completed Billable entries
            new TimeEntry
            {
                WorkItemId = crm101.Id,
                EmployeeId = alex.Id,
                StartedAt = DateTimeOffset.UtcNow.AddDays(-3).AddHours(-8),
                EndedAt = DateTimeOffset.UtcNow.AddDays(-3),
                DurationMinutes = 480,
                LoggedDate = today.AddDays(-3),
                IsRunning = false,
                IsBillable = true,
                Notes = "Implemented CrmDbContext and EF Core mappings"
            },
            new TimeEntry
            {
                WorkItemId = crm101.Id,
                EmployeeId = alex.Id,
                StartedAt = DateTimeOffset.UtcNow.AddDays(-2).AddHours(-4),
                EndedAt = DateTimeOffset.UtcNow.AddDays(-2),
                DurationMinutes = 240,
                LoggedDate = today.AddDays(-2),
                IsRunning = false,
                IsBillable = true,
                Notes = "Added SQLite fallback and WAL connection probe"
            },
            new TimeEntry
            {
                WorkItemId = crm102.Id,
                EmployeeId = alex.Id,
                StartedAt = DateTimeOffset.UtcNow.AddDays(-1).AddHours(-4),
                EndedAt = DateTimeOffset.UtcNow.AddDays(-1),
                DurationMinutes = 240,
                LoggedDate = yesterday,
                IsRunning = false,
                IsBillable = true,
                Notes = "Unit tests for review transitions"
            },
            new TimeEntry
            {
                WorkItemId = crm104.Id,
                EmployeeId = maya.Id,
                StartedAt = DateTimeOffset.UtcNow.AddHours(-5),
                EndedAt = DateTimeOffset.UtcNow.AddHours(-2),
                DurationMinutes = 180,
                LoggedDate = today,
                IsRunning = false,
                IsBillable = true,
                Notes = "Designed column headers and cards in MUI"
            },
            // Completed Non-Billable entry
            new TimeEntry
            {
                WorkItemId = crm101.Id,
                EmployeeId = alex.Id,
                StartedAt = DateTimeOffset.UtcNow.AddDays(-2).AddHours(-7),
                EndedAt = DateTimeOffset.UtcNow.AddDays(-2).AddHours(-5),
                DurationMinutes = 120,
                LoggedDate = today.AddDays(-2),
                IsRunning = false,
                IsBillable = false,
                Notes = "Internal tooling setup and Docker compose diagnostics"
            }
        );

        // 6. Weekly Plans
        var currentMonday = WeeklyPlan.NormalizeToMonday(today);

        _context.WeeklyPlans.AddRange(
            new WeeklyPlan
            {
                EmployeeId = alex.Id,
                ProjectId = projectCrm.Id,
                WeekStartDate = currentMonday,
                GoalsMarkdown = "### Weekly Goals\n- Finalize dual-mode database\n- Build sync orchestrator with SHA-256 loop prevention\n- Implement Hangfire cutoff background job",
                FocusAreasJson = "[\"Database Reliability\",\"External Sync\",\"Hangfire Jobs\"]",
                KeyDeliverables = "Clean build with 0 warnings, all unit and integration tests passing",
                Status = WeeklyPlanStatus.Submitted
            },
            new WeeklyPlan
            {
                EmployeeId = maya.Id,
                ProjectId = projectCrm.Id,
                WeekStartDate = currentMonday,
                GoalsMarkdown = "### Frontend Sprint Goals\n- Deliver interactive Kanban board with dnd-kit\n- Fix timer drawer re-rendering issue\n- Implement Skills Matrix UI",
                FocusAreasJson = "[\"Kanban UI\",\"Timer Drawer\",\"Skills Matrix\"]",
                KeyDeliverables = "Smooth drag-and-drop experience, responsive drawer layout",
                Status = WeeklyPlanStatus.Submitted
            },
            new WeeklyPlan
            {
                EmployeeId = sarah.Id,
                ProjectId = projectCrm.Id,
                WeekStartDate = currentMonday,
                GoalsMarkdown = "### Architecture & Governance\n- Review M2 infrastructure implementation\n- Ensure RBAC coverage and zero regressions",
                FocusAreasJson = "[\"Architecture Review\",\"Code Quality\",\"Security\"]",
                KeyDeliverables = "Approval of external sync pipeline",
                Status = WeeklyPlanStatus.Completed
            }
        );

        // 7. Daily Plans & Items
        var alexDailyPlan = new DailyPlan
        {
            EmployeeId = alex.Id,
            PlanDate = today,
            SubmittedAt = DateTimeOffset.UtcNow.AddHours(-2),
            IsSubmittedOnTime = true,
            Blockers = null,
            SummaryNotes = "Focusing on sync orchestrator and Hangfire cutoff reminder job."
        };

        alexDailyPlan.Items.Add(new DailyPlanItem
        {
            WorkItemId = crm107.Id,
            CustomTaskTitle = "Configure Hangfire recurring job",
            PlannedHours = 4.0m,
            GoalNote = "Ensure timezone fallback and weekend exclusion work cleanly",
            IsCompleted = false
        });

        alexDailyPlan.Items.Add(new DailyPlanItem
        {
            WorkItemId = fin201.Id,
            CustomTaskTitle = "Billing margin formulas",
            PlannedHours = 3.5m,
            GoalNote = "Calculate gross profit correctly",
            IsCompleted = false
        });

        var mayaDailyPlan = new DailyPlan
        {
            EmployeeId = maya.Id,
            PlanDate = today,
            SubmittedAt = DateTimeOffset.UtcNow.AddHours(-1),
            IsSubmittedOnTime = true,
            Blockers = "Waiting on final review decision for CRM-105",
            SummaryNotes = "Kanban drag-and-drop in progress."
        };

        mayaDailyPlan.Items.Add(new DailyPlanItem
        {
            WorkItemId = crm104.Id,
            CustomTaskTitle = "Kanban card styling",
            PlannedHours = 4.0m,
            GoalNote = "Implement MUI card styling",
            IsCompleted = false
        });

        mayaDailyPlan.Items.Add(new DailyPlanItem
        {
            WorkItemId = crm105.Id,
            CustomTaskTitle = "Timer stop handler",
            PlannedHours = 3.0m,
            GoalNote = "Ensure timer state synchronizes with Redux store",
            IsCompleted = false
        });

        var sarahYesterdayPlan = new DailyPlan
        {
            EmployeeId = sarah.Id,
            PlanDate = yesterday,
            SubmittedAt = DateTimeOffset.UtcNow.AddDays(-1).AddHours(-4),
            IsSubmittedOnTime = true,
            SummaryNotes = "Reviewed PR for Reviewer state machine."
        };

        _context.DailyPlans.AddRange(alexDailyPlan, mayaDailyPlan, sarahYesterdayPlan);

        // 8. Planning Cutoff Configs
        _context.PlanningCutoffConfigs.AddRange(
            // Global default
            new PlanningCutoffConfig
            {
                ProjectId = null,
                CutoffTimeLocal = new TimeOnly(10, 0, 0),
                TimeZoneId = "UTC",
                IsReminderEnabled = true,
                ExcludedDaysOfWeekJson = "[0, 6]",
                UpdatedAt = DateTimeOffset.UtcNow
            },
            // CRM project custom
            new PlanningCutoffConfig
            {
                ProjectId = projectCrm.Id,
                CutoffTimeLocal = new TimeOnly(9, 30, 0),
                TimeZoneId = "America/New_York",
                IsReminderEnabled = true,
                ExcludedDaysOfWeekJson = "[0, 6]",
                UpdatedAt = DateTimeOffset.UtcNow
            }
        );

        // 9. Daily Plan Reminder Log (Idempotency demonstration)
        _context.DailyPlanReminderLogs.Add(new DailyPlanReminderLog
        {
            EmployeeId = maya.Id,
            PlanDate = yesterday,
            ReminderSentAt = DateTimeOffset.UtcNow.AddDays(-1),
            RecipientEmail = maya.Email,
            EmailDeliveryStatus = "Sent",
            ErrorMessage = null
        });

        // 10. Sync Conflict (Deduplication engine candidate)
        _context.SyncConflicts.Add(new SyncConflict
        {
            ProjectId = projectCrm.Id,
            LocalWorkItemId = crm104.Id,
            ExternalSource = ExternalSourceType.AzureDevOps,
            ExternalId = "9021",
            ExternalTitle = "Kanban Board Column Reordering Interaction",
            ExternalDescription = "Simulated remote task for Kanban board drag reordering.",
            ExternalStatus = "Active",
            ExternalAssigneeEmail = "maya.patel@companycrm.local",
            SimilarityScore = 0.88,
            ConflictType = SyncConflictType.DuplicateCandidate,
            Status = ConflictStatus.Pending,
            ResolutionAction = ConflictResolutionAction.Unresolved,
            DetectedAt = DateTimeOffset.UtcNow.AddHours(-1)
        });

        // 11. Email Notification Logs
        _context.EmailNotificationLogs.AddRange(
            new EmailNotificationLog
            {
                RecipientEmail = sarah.Email,
                RecipientName = sarah.FullName,
                Subject = "[CompanyCRM] Review Requested: CRM-107 - Automated Daily Cutoff Check Background Worker",
                BodyHtml = "<p>Review requested by Alex Rivera</p>",
                TriggerEvent = "ReviewerAssigned",
                WorkItemId = crm107.Id,
                SentAt = DateTimeOffset.UtcNow.AddHours(-2),
                Success = true
            },
            new EmailNotificationLog
            {
                RecipientEmail = alex.Email,
                RecipientName = alex.FullName,
                Subject = "[CompanyCRM] Clarification Requested: CRM-108 - PostgreSQL DateOnly Conversion Offset in Hangfire Cron",
                BodyHtml = "<p>Clarification requested by David Chen</p>",
                TriggerEvent = "ReviewClarificationRequested",
                WorkItemId = crm108.Id,
                SentAt = DateTimeOffset.UtcNow.AddHours(-3),
                Success = true
            },
            new EmailNotificationLog
            {
                RecipientEmail = maya.Email,
                RecipientName = maya.FullName,
                Subject = $"[CompanyCRM] Action Required: Missing Daily Plan for {yesterday:yyyy-MM-dd}",
                BodyHtml = "<p>Please submit your daily check-in plan</p>",
                TriggerEvent = "DailyPlanCutoffReminder",
                SentAt = DateTimeOffset.UtcNow.AddDays(-1),
                Success = true
            }
        );

        await _context.SaveChangesAsync(ct);
        _logger.LogInformation("Database successfully seeded with demo dataset!");
    }

    public static async Task SeedDemoDataAsync(IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
        await context.Database.EnsureCreatedAsync(ct);
        var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();
        await seeder.SeedAsync(ct);
    }
}
