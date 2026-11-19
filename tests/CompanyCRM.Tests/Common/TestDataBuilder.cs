namespace CompanyCRM.Tests.Common;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using CompanyCRM.Domain.Entities;
using CompanyCRM.Domain.Enums;
using CompanyCRM.Domain.ValueObjects;
using CompanyCRM.Application.Interfaces;
using CompanyCRM.Application.DTOs;
using CompanyCRM.Application.Services;

/// <summary>
/// Test-isolated EF Core DbContext mapping Domain entities to an in-memory database.
/// Provides genuine relational persistence for unit and integration testing.
/// </summary>
public class TestCrmDbContext : DbContext
{
    public TestCrmDbContext(DbContextOptions<TestCrmDbContext> options) : base(options) { }

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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<WorkItem>(b =>
        {
            b.HasOne(w => w.CreatedBy)
                .WithMany(e => e.CreatedWorkItems)
                .HasForeignKey(w => w.CreatedById)
                .OnDelete(DeleteBehavior.Restrict);

            b.HasOne(w => w.Assignee)
                .WithMany(e => e.AssignedWorkItems)
                .HasForeignKey(w => w.AssigneeId)
                .OnDelete(DeleteBehavior.SetNull);

            b.HasOne(w => w.Reviewer)
                .WithMany(e => e.ReviewedWorkItems)
                .HasForeignKey(w => w.ReviewerId)
                .OnDelete(DeleteBehavior.SetNull);

            b.HasOne(w => w.Project)
                .WithMany(p => p.WorkItems)
                .HasForeignKey(w => w.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ProjectMember>(b =>
        {
            b.HasOne(pm => pm.Project)
                .WithMany(p => p.Members)
                .HasForeignKey(pm => pm.ProjectId);

            b.HasOne(pm => pm.Employee)
                .WithMany(e => e.ProjectMemberships)
                .HasForeignKey(pm => pm.EmployeeId);
        });

        modelBuilder.Entity<TimeEntry>(b =>
        {
            b.HasOne(t => t.WorkItem)
                .WithMany(w => w.TimeEntries)
                .HasForeignKey(t => t.WorkItemId);

            b.HasOne(t => t.Employee)
                .WithMany(e => e.TimeEntries)
                .HasForeignKey(t => t.EmployeeId);
        });

        modelBuilder.Entity<EmployeeSkill>(b =>
        {
            b.HasOne(s => s.Employee)
                .WithMany(e => e.Skills)
                .HasForeignKey(s => s.EmployeeId);
        });

        modelBuilder.Entity<WeeklyPlan>(b =>
        {
            b.HasOne(wp => wp.Employee)
                .WithMany(e => e.WeeklyPlans)
                .HasForeignKey(wp => wp.EmployeeId);

            b.HasOne(wp => wp.Project)
                .WithMany(p => p.WeeklyPlans)
                .HasForeignKey(wp => wp.ProjectId);
        });

        modelBuilder.Entity<DailyPlan>(b =>
        {
            b.HasOne(dp => dp.Employee)
                .WithMany(e => e.DailyPlans)
                .HasForeignKey(dp => dp.EmployeeId);

            b.HasMany(dp => dp.Items)
                .WithOne(dpi => dpi.DailyPlan)
                .HasForeignKey(dpi => dpi.DailyPlanId);
        });

        modelBuilder.Entity<DailyPlanReminderLog>(b =>
        {
            b.HasOne(l => l.Employee)
                .WithMany(e => e.ReminderLogs)
                .HasForeignKey(l => l.EmployeeId);
        });

        modelBuilder.Entity<WorkItemHistory>(b =>
        {
            b.HasOne(h => h.WorkItem)
                .WithMany(w => w.Histories)
                .HasForeignKey(h => h.WorkItemId);

            b.HasOne(h => h.ChangedBy)
                .WithMany(e => e.HistoryEntries)
                .HasForeignKey(h => h.ChangedById);
        });

        modelBuilder.Entity<SyncConflict>(b =>
        {
            b.HasOne(c => c.Project)
                .WithMany(p => p.SyncConflicts)
                .HasForeignKey(c => c.ProjectId);

            b.HasOne(c => c.LocalWorkItem)
                .WithMany(w => w.SyncConflicts)
                .HasForeignKey(c => c.LocalWorkItemId);
        });

        modelBuilder.Entity<PlanningCutoffConfig>(b =>
        {
            b.HasOne(c => c.Project)
                .WithMany(p => p.CutoffConfigs)
                .HasForeignKey(c => c.ProjectId);
        });
    }
}

/// <summary>
/// Deterministic TimeProvider for testing time-sensitive operations without OS clock coupling.
/// </summary>
public class TestTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow;

    public TestTimeProvider(DateTimeOffset initialUtcNow)
    {
        _utcNow = initialUtcNow;
    }

    public void SetUtcNow(DateTimeOffset utcNow) => _utcNow = utcNow;
    public void Advance(TimeSpan delta) => _utcNow = _utcNow.Add(delta);
    public override DateTimeOffset GetUtcNow() => _utcNow;
}

/// <summary>
/// Central test data builder providing shared factories, in-memory context initializers,
/// and service harnesses for all test suites.
/// </summary>
public static class TestDataBuilder
{
    public static TestCrmDbContext CreateInMemoryDbContext(string? dbName = null)
    {
        var options = new DbContextOptionsBuilder<TestCrmDbContext>()
            .UseInMemoryDatabase(databaseName: dbName ?? Guid.NewGuid().ToString())
            .EnableSensitiveDataLogging()
            .Options;

        var context = new TestCrmDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    public static Project CreateProject(
        Guid? id = null,
        string code = "CRM",
        string name = "Company CRM Platform",
        bool enableSync = true,
        bool isSimulatedMode = true,
        string? pat = null)
    {
        return new Project
        {
            Id = id ?? Guid.NewGuid(),
            Code = code,
            Name = name,
            Description = "Internal CRM and project management platform",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            ExternalSource = ExternalSourceType.AzureDevOps,
            ExternalOrgUrl = "https://dev.azure.com/companyorg",
            ExternalProjectName = "CompanyProject",
            ExternalPat = pat,
            IsSyncEnabled = enableSync,
            IsSimulatedMode = isSimulatedMode
        };
    }

    public static Employee CreateEmployee(
        Guid? id = null,
        string email = "jane.dev@company.com",
        string fullName = "Jane Dev",
        AppRole role = AppRole.Employee,
        decimal hourlyCostRate = 50.00m,
        decimal hourlyBillableRate = 120.00m,
        decimal weeklyCapacityHours = 40.0m)
    {
        return new Employee
        {
            Id = id ?? Guid.NewGuid(),
            Email = email,
            FullName = fullName,
            Department = "Engineering",
            JobTitle = "Software Engineer",
            Role = role,
            HourlyCostRate = hourlyCostRate,
            HourlyBillableRate = hourlyBillableRate,
            WeeklyCapacityHours = weeklyCapacityHours,
            AnnualBaseSalary = 100000.00m,
            TargetUtilizationPercent = 80.0m,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    public static WorkItem CreateWorkItem(
        Guid projectId,
        Guid createdById,
        string code = "CRM-101",
        string title = "Implement OAuth2 login flow",
        WorkItemType type = WorkItemType.Task,
        WorkItemStatus status = WorkItemStatus.Draft,
        ReviewStatus reviewStatus = ReviewStatus.NotRequired,
        Guid? reviewerId = null,
        Guid? assigneeId = null,
        string? externalId = null,
        string? contentHash = null)
    {
        return new WorkItem
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            CreatedById = createdById,
            Code = code,
            Title = title,
            Description = "Detailed specifications for task implementation.",
            Type = type,
            Status = status,
            Priority = WorkItemPriority.High,
            ReviewStatus = reviewStatus,
            ReviewerId = reviewerId,
            AssigneeId = assigneeId,
            ExternalSource = externalId != null ? ExternalSourceType.AzureDevOps : ExternalSourceType.None,
            ExternalId = externalId,
            ExternalUrl = externalId != null ? $"https://dev.azure.com/companyorg/CompanyProject/_workitems/edit/{externalId}" : null,
            LastSyncedContentHash = contentHash,
            LastSyncOrigin = externalId != null ? SyncOrigin.LocalCrm : SyncOrigin.LocalCrm,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    public static TimeEntry CreateTimeEntry(
        Guid workItemId,
        Guid employeeId,
        int durationMinutes,
        bool isBillable = true,
        DateOnly? loggedDate = null)
    {
        return new TimeEntry
        {
            Id = Guid.NewGuid(),
            WorkItemId = workItemId,
            EmployeeId = employeeId,
            DurationMinutes = durationMinutes,
            IsBillable = isBillable,
            IsRunning = false,
            LoggedDate = loggedDate ?? DateOnly.FromDateTime(DateTime.UtcNow),
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-durationMinutes),
            EndedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    public static TimeEntry CreateTimeEntry(
        WorkItem workItem,
        Employee employee,
        int durationMinutes,
        bool isBillable = true,
        DateOnly? loggedDate = null)
    {
        return new TimeEntry
        {
            Id = Guid.NewGuid(),
            WorkItemId = workItem.Id,
            WorkItem = workItem,
            EmployeeId = employee.Id,
            Employee = employee,
            DurationMinutes = durationMinutes,
            IsBillable = isBillable,
            IsRunning = false,
            LoggedDate = loggedDate ?? DateOnly.FromDateTime(DateTime.UtcNow),
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-durationMinutes),
            EndedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    public static SyncConflict CreateConflict(
        Guid projectId,
        Guid? localWorkItemId,
        string externalId = "ADO-2002",
        string externalTitle = "Implement OAuth2 login flow",
        double similarityScore = 1.0,
        ConflictStatus status = ConflictStatus.Pending)
    {
        return new SyncConflict
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            LocalWorkItemId = localWorkItemId,
            ExternalSource = ExternalSourceType.AzureDevOps,
            ExternalId = externalId,
            ExternalTitle = externalTitle,
            ExternalDescription = "Remote description from Azure DevOps",
            ExternalStatus = "To Do",
            SimilarityScore = similarityScore,
            ConflictType = SyncConflictType.DuplicateCandidate,
            Status = status,
            ResolutionAction = ConflictResolutionAction.Unresolved,
            DetectedAt = DateTimeOffset.UtcNow
        };
    }

    public static PlanningCutoffConfig CreateCutoffConfig(
        TimeOnly cutoffTimeLocal,
        string timeZoneId = "UTC",
        bool isReminderEnabled = true,
        string excludedDaysJson = "[0, 6]")
    {
        return new PlanningCutoffConfig
        {
            Id = Guid.NewGuid(),
            CutoffTimeLocal = cutoffTimeLocal,
            TimeZoneId = timeZoneId,
            IsReminderEnabled = isReminderEnabled,
            ExcludedDaysOfWeekJson = excludedDaysJson,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }
}

/// <summary>
/// Review service harness implementing IWorkItemReviewService for testing.
/// </summary>
public class TestWorkItemReviewService : IWorkItemReviewService
{
    private readonly TestCrmDbContext _dbContext;
    private readonly IEmailNotificationService _emailService;
    private readonly ISyncOrchestratorService _syncOrchestrator;
    private readonly Guid? _callerId;

    public TestWorkItemReviewService(
        TestCrmDbContext dbContext,
        IEmailNotificationService emailService,
        ISyncOrchestratorService syncOrchestrator,
        Guid? callerId = null)
    {
        _dbContext = dbContext;
        _emailService = emailService;
        _syncOrchestrator = syncOrchestrator;
        _callerId = callerId;
    }

    public async Task<WorkItemDto> AssignReviewerAsync(Guid workItemId, Guid reviewerId, CancellationToken ct = default)
    {
        var item = await _dbContext.WorkItems.FindAsync(new object[] { workItemId }, ct)
            ?? throw new InvalidOperationException($"Work item {workItemId} not found");

        var reviewer = await _dbContext.Employees.FindAsync(new object[] { reviewerId }, ct)
            ?? throw new InvalidOperationException($"Reviewer {reviewerId} not found");

        item.AssignReviewer(reviewerId);
        await _dbContext.SaveChangesAsync(ct);

        await _emailService.SendReviewAssignedEmailAsync(
            reviewer.Email, reviewer.FullName, item.Code, item.Title, item.Id, ct);

        return ToDto(item);
    }

    public Task<WorkItemDto> ApproveReviewAsync(Guid workItemId, Guid reviewerId, string? approvalNote = null, CancellationToken ct = default)
        => ApproveReviewAsync(workItemId, reviewerId, WorkItemStatus.Todo, approvalNote, ct);

    public async Task<WorkItemDto> ApproveReviewAsync(Guid workItemId, Guid reviewerId, WorkItemStatus startingStatus, string? approvalNote = null, CancellationToken ct = default)
    {
        if (startingStatus != WorkItemStatus.Todo && startingStatus != WorkItemStatus.Backlog)
        {
            throw new ArgumentException("StartingStatus must be either Todo or Backlog.", nameof(startingStatus));
        }

        if (_callerId.HasValue)
        {
            var caller = await _dbContext.Employees.FindAsync(new object[] { _callerId.Value }, ct);
            if (caller != null && caller.Role == AppRole.Employee)
            {
                throw new UnauthorizedAccessException("Employee role cannot perform review actions. Reviewer role required.");
            }
        }

        var item = await _dbContext.WorkItems.FindAsync(new object[] { workItemId }, ct)
            ?? throw new InvalidOperationException($"Work item {workItemId} not found");

        if (item.ReviewStatus == ReviewStatus.Denied)
        {
            throw new InvalidOperationException("Cannot approve a denied work item.");
        }

        if (item.ReviewStatus == ReviewStatus.Approved)
        {
            throw new InvalidOperationException("Work item review has already been approved.");
        }

        item.ApproveReview(reviewerId, startingStatus);
        item.ReviewLatestComment = approvalNote;
        await _dbContext.SaveChangesAsync(ct);

        await _syncOrchestrator.PushWorkItemToExternalAsync(item.Id, ct);

        return ToDto(item);
    }

    public async Task<WorkItemDto> RequestClarificationAsync(Guid workItemId, Guid reviewerId, string clarificationComment, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(clarificationComment) || clarificationComment.Trim().Length < 5)
        {
            throw new ArgumentException("Clarification comment must contain at least 5 characters.", nameof(clarificationComment));
        }

        var item = await _dbContext.WorkItems.FindAsync(new object[] { workItemId }, ct)
            ?? throw new InvalidOperationException($"Work item {workItemId} not found");

        var creator = await _dbContext.Employees.FindAsync(new object[] { item.CreatedById }, ct);

        item.RequestClarification(reviewerId, clarificationComment);
        await _dbContext.SaveChangesAsync(ct);

        if (creator != null)
        {
            await _emailService.SendReviewDecisionEmailAsync(
                creator.Email, creator.FullName, item.Code, item.Title, ReviewStatus.ClarificationNeeded, clarificationComment, ct);
        }

        return ToDto(item);
    }

    public async Task<WorkItemDto> DenyReviewAsync(Guid workItemId, Guid reviewerId, string denialReason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(denialReason) || denialReason.Trim().Length < 5)
        {
            throw new ArgumentException("Denial reason must contain at least 5 characters.", nameof(denialReason));
        }

        var item = await _dbContext.WorkItems.FindAsync(new object[] { workItemId }, ct)
            ?? throw new InvalidOperationException($"Work item {workItemId} not found");

        var creator = await _dbContext.Employees.FindAsync(new object[] { item.CreatedById }, ct);

        item.DenyReview(reviewerId, denialReason);
        await _dbContext.SaveChangesAsync(ct);

        if (creator != null)
        {
            await _emailService.SendReviewDecisionEmailAsync(
                creator.Email, creator.FullName, item.Code, item.Title, ReviewStatus.Denied, denialReason, ct);
        }

        return ToDto(item);
    }

    public async Task<IReadOnlyList<WorkItemReviewQueueDto>> GetReviewQueueAsync(Guid reviewerId, Guid? projectId = null, CancellationToken ct = default)
    {
        var query = _dbContext.WorkItems.AsQueryable()
            .Where(w => w.ReviewStatus == ReviewStatus.Pending && w.ReviewerId == reviewerId);

        if (projectId.HasValue)
        {
            query = query.Where(w => w.ProjectId == projectId.Value);
        }

        var items = await query.ToListAsync(ct);
        return items.Select(w => new WorkItemReviewQueueDto(
            w.Id,
            w.Code,
            w.Title,
            w.Description,
            w.ProjectId,
            "CRM",
            "Company CRM Platform",
            w.Type,
            w.Priority,
            w.CreatedById,
            "Creator",
            w.ReviewerId,
            "Reviewer",
            w.ReviewStatus,
            w.ReviewLatestComment,
            w.CreatedAt)).ToList();
    }

    public async Task<WorkItemDto> UpdateWorkItemStatusAsync(Guid workItemId, WorkItemStatus newStatus, CancellationToken ct = default)
    {
        var item = await _dbContext.WorkItems.FindAsync(new object[] { workItemId }, ct)
            ?? throw new InvalidOperationException($"Work item {workItemId} not found");

        if (!item.CanTransitionKanban())
        {
            throw new InvalidOperationException("Work item cannot transition Kanban columns until approved.");
        }

        item.Status = newStatus;
        await _dbContext.SaveChangesAsync(ct);
        return ToDto(item);
    }

    private static WorkItemDto ToDto(WorkItem item)
    {
        return new WorkItemDto(
            item.Id,
            item.ProjectId,
            "CRM",
            item.Code,
            item.Title,
            item.Description,
            item.Type,
            item.Status,
            item.Priority,
            item.EstimatedHours,
            item.StoryPoints,
            item.SprintName,
            item.BoardPosition,
            item.CreatedById,
            "Creator",
            item.AssigneeId,
            item.Assignee?.FullName,
            item.Assignee?.Email,
            item.ReviewerId,
            item.Reviewer?.FullName,
            item.ReviewStatus,
            item.ReviewLatestComment,
            item.ReviewedAt,
            item.ExternalSource,
            item.ExternalId,
            item.ExternalUrl,
            item.LastSyncedAt,
            item.LastSyncedContentHash,
            item.LastSyncOrigin,
            0,
            0,
            false,
            item.CreatedAt,
            item.UpdatedAt);
    }
}

/// <summary>
/// Sync orchestrator test harness implementing ISyncOrchestratorService.
/// </summary>
public class TestSyncOrchestratorService : ISyncOrchestratorService
{
    private readonly TestCrmDbContext _dbContext;
    private readonly IIssueTrackerProvider _provider;

    public TestSyncOrchestratorService(TestCrmDbContext dbContext, IIssueTrackerProvider provider)
    {
        _dbContext = dbContext;
        _provider = provider;
    }

    public async Task<SyncResultDto> PushWorkItemToExternalAsync(Guid workItemId, CancellationToken ct = default)
    {
        var item = await _dbContext.WorkItems.FindAsync(new object[] { workItemId }, ct)
            ?? throw new InvalidOperationException($"Work item {workItemId} not found");

        var project = await _dbContext.Projects.FindAsync(new object[] { item.ProjectId }, ct)
            ?? throw new InvalidOperationException($"Project {item.ProjectId} not found");

        if (!item.CanPushToExternal())
        {
            throw new InvalidOperationException("Cannot push denied or unapproved work item to external issue tracker.");
        }

        var assignee = item.AssigneeId.HasValue
            ? await _dbContext.Employees.FindAsync(new object[] { item.AssigneeId.Value }, ct)
            : null;

        var hash = SyncContentHasher.ComputeHash(
            item.Title,
            item.Description,
            item.Status,
            item.Type,
            assignee?.Email);

        string externalId;
        string externalUrl;

        if (project.IsSimulatedMode || string.IsNullOrWhiteSpace(project.ExternalPat))
        {
            // Deterministic sandbox fallback
            var cleanedCode = item.Code.Replace("-", "");
            externalId = $"ADO-SIM-{cleanedCode}";
            externalUrl = $"https://dev.azure.com/simulated-org/CompanyProject/_workitems/edit/{externalId}";
        }
        else
        {
            var config = new ProjectExternalConfig(
                project.Id,
                project.ExternalSource,
                project.ExternalOrgUrl ?? string.Empty,
                project.ExternalProjectName ?? string.Empty,
                project.ExternalPat,
                project.IsSyncEnabled,
                project.IsSimulatedMode,
                project.LastSyncRunAt);

            var externalDto = await _provider.PushWorkItemAsync(config, item, ct);
            externalId = externalDto.ExternalId;
            externalUrl = externalDto.ExternalUrl;
        }

        item.ExternalId = externalId;
        item.ExternalUrl = externalUrl;
        item.LastSyncedContentHash = hash;
        item.LastSyncOrigin = SyncOrigin.LocalCrm;
        item.LastSyncedAt = DateTimeOffset.UtcNow;
        item.UpdatedAt = DateTimeOffset.UtcNow;

        await _dbContext.SaveChangesAsync(ct);

        return new SyncResultDto(true, item.Id, externalId, externalUrl, hash, null, SyncOrigin.LocalCrm);
    }

    public async Task<WebhookProcessingResultDto> ProcessInboundWebhookAsync(
        ExternalSourceType source,
        string rawPayload,
        IDictionary<string, string> headers,
        CancellationToken ct = default)
    {
        var parsedDto = await _provider.ParseWebhookPayloadAsync(rawPayload, headers, ct);
        if (parsedDto == null)
        {
            return new WebhookProcessingResultDto("Error", null, null, false, "Failed to parse webhook payload");
        }

        var localItem = await _dbContext.WorkItems.FirstOrDefaultAsync(w => w.ExternalId == parsedDto.ExternalId, ct);

        if (localItem == null)
        {
            // Novel remote item arrived: import as new work item
            var defaultProject = await _dbContext.Projects.FirstOrDefaultAsync(ct)
                ?? throw new InvalidOperationException("No project available for webhook import");

            var defaultCreator = await _dbContext.Employees.FirstOrDefaultAsync(ct)
                ?? throw new InvalidOperationException("No employee available for webhook import");

            var status = ParseStatus(parsedDto.Status);
            var type = ParseType(parsedDto.Type);

            var newHash = SyncContentHasher.ComputeHash(
                parsedDto.Title,
                parsedDto.Description,
                status,
                type,
                parsedDto.AssigneeEmail);

            var newItem = new WorkItem
            {
                Id = Guid.NewGuid(),
                ProjectId = defaultProject.Id,
                CreatedById = defaultCreator.Id,
                Code = $"EXT-{parsedDto.ExternalId}",
                Title = parsedDto.Title,
                Description = parsedDto.Description,
                Status = status,
                Type = type,
                ExternalSource = source,
                ExternalId = parsedDto.ExternalId,
                ExternalUrl = parsedDto.ExternalUrl,
                LastSyncedContentHash = newHash,
                LastSyncOrigin = SyncOrigin.ExternalWebhook,
                LastSyncedAt = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            _dbContext.WorkItems.Add(newItem);
            await _dbContext.SaveChangesAsync(ct);

            return new WebhookProcessingResultDto("Applied", parsedDto.ExternalId, newItem.Id, false, "Imported novel remote work item");
        }

        var remoteStatus = ParseStatus(parsedDto.Status);
        var remoteType = ParseType(parsedDto.Type);

        var incomingHash = SyncContentHasher.ComputeHash(
            parsedDto.Title,
            parsedDto.Description,
            remoteStatus,
            remoteType,
            parsedDto.AssigneeEmail);

        if (localItem.LastSyncedContentHash == incomingHash)
        {
            // Echo detected! No database modification
            return new WebhookProcessingResultDto("IgnoredEcho", parsedDto.ExternalId, localItem.Id, true, "Echo ignored; content matches last synced hash");
        }

        // Genuine remote update
        localItem.Title = parsedDto.Title;
        localItem.Description = parsedDto.Description;
        localItem.Status = remoteStatus;
        localItem.Type = remoteType;
        localItem.LastSyncedContentHash = incomingHash;
        localItem.LastSyncOrigin = SyncOrigin.ExternalWebhook;
        localItem.LastSyncedAt = DateTimeOffset.UtcNow;
        localItem.UpdatedAt = DateTimeOffset.UtcNow;

        _dbContext.WorkItemHistories.Add(new WorkItemHistory
        {
            Id = Guid.NewGuid(),
            WorkItemId = localItem.Id,
            ChangedById = localItem.CreatedById,
            Action = "SyncedFromExternal",
            FieldName = "Status",
            NewValue = parsedDto.Status,
            ChangedAt = DateTimeOffset.UtcNow
        });

        await _dbContext.SaveChangesAsync(ct);

        return new WebhookProcessingResultDto("Applied", parsedDto.ExternalId, localItem.Id, false, "Remote update applied");
    }

    public async Task<ReconciliationResultDto> RunReconciliationAsync(Guid? projectId = null, CancellationToken ct = default)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var query = _dbContext.Projects.AsQueryable();
        if (projectId.HasValue) query = query.Where(p => p.Id == projectId.Value);
        var project = await query.FirstOrDefaultAsync(ct);

        if (project == null)
        {
            return new ReconciliationResultDto(projectId, 0, 0, 0, 0, new[] { "Project not found" }, startedAt, DateTimeOffset.UtcNow);
        }

        var config = new ProjectExternalConfig(
            project.Id,
            project.ExternalSource,
            project.ExternalOrgUrl ?? string.Empty,
            project.ExternalProjectName ?? string.Empty,
            project.ExternalPat,
            project.IsSyncEnabled,
            project.IsSimulatedMode,
            project.LastSyncRunAt);

        var remoteItems = await _provider.FetchRemoteWorkItemsAsync(config, project.LastSyncRunAt, ct);
        int conflicts = 0;

        foreach (var remote in remoteItems)
        {
            var existingByExtId = await _dbContext.WorkItems.FirstOrDefaultAsync(w => w.ExternalId == remote.ExternalId, ct);
            if (existingByExtId != null) continue;

            // Check unlinked items for duplicate candidate by title similarity
            var unlinkedItems = await _dbContext.WorkItems
                .Where(w => w.ProjectId == project.Id && w.ExternalId == null)
                .ToListAsync(ct);

            foreach (var local in unlinkedItems)
            {
                var sim = ComputeTitleSimilarity(local.Title, remote.Title);
                if (sim >= 0.85)
                {
                    _dbContext.SyncConflicts.Add(new SyncConflict
                    {
                        Id = Guid.NewGuid(),
                        ProjectId = project.Id,
                        LocalWorkItemId = local.Id,
                        ExternalSource = project.ExternalSource,
                        ExternalId = remote.ExternalId,
                        ExternalTitle = remote.Title,
                        ExternalDescription = remote.Description,
                        ExternalStatus = remote.Status,
                        SimilarityScore = sim,
                        ConflictType = SyncConflictType.DuplicateCandidate,
                        Status = ConflictStatus.Pending,
                        ResolutionAction = ConflictResolutionAction.Unresolved,
                        DetectedAt = DateTimeOffset.UtcNow
                    });
                    conflicts++;
                    break;
                }
            }
        }

        await _dbContext.SaveChangesAsync(ct);
        return new ReconciliationResultDto(projectId, remoteItems.Count, 0, conflicts, 0, Array.Empty<string>(), startedAt, DateTimeOffset.UtcNow);
    }

    public async Task<IReadOnlyList<SyncConflictDto>> GetPendingConflictsAsync(Guid? projectId = null, CancellationToken ct = default)
    {
        var query = _dbContext.SyncConflicts.Where(c => c.Status == ConflictStatus.Pending);
        if (projectId.HasValue) query = query.Where(c => c.ProjectId == projectId.Value);
        var list = await query.ToListAsync(ct);

        return list.Select(c => new SyncConflictDto(
            c.Id,
            c.ProjectId,
            c.LocalWorkItemId,
            "CRM-LOCAL",
            "Local Item",
            c.ExternalSource,
            c.ExternalId,
            c.ExternalTitle,
            c.ExternalDescription,
            c.ExternalStatus,
            c.ExternalAssigneeEmail,
            c.SimilarityScore,
            c.ConflictType,
            c.Status,
            c.ResolutionAction,
            c.ResolutionNotes,
            c.DetectedAt,
            c.ResolvedAt)).ToList();
    }

    public async Task<ConflictResolutionResultDto> ResolveConflictAsync(Guid conflictId, ConflictResolutionAction action, CancellationToken ct = default)
    {
        var conflict = await _dbContext.SyncConflicts.FindAsync(new object[] { conflictId }, ct)
            ?? throw new InvalidOperationException($"Conflict {conflictId} not found");

        if (conflict.Status == ConflictStatus.Resolved)
        {
            throw new InvalidOperationException("Conflict is already resolved.");
        }

        conflict.Status = ConflictStatus.Resolved;
        conflict.ResolutionAction = action;
        conflict.ResolvedAt = DateTimeOffset.UtcNow;

        WorkItem? localItem = null;
        if (conflict.LocalWorkItemId.HasValue)
        {
            localItem = await _dbContext.WorkItems.FindAsync(new object[] { conflict.LocalWorkItemId.Value }, ct);
        }

        switch (action)
        {
            case ConflictResolutionAction.Merge:
                if (localItem != null)
                {
                    localItem.ExternalId = conflict.ExternalId;
                    localItem.LastSyncedContentHash = SyncContentHasher.ComputeHash(
                        localItem.Title, localItem.Description, localItem.Status, localItem.Type, null);
                    localItem.LastSyncOrigin = SyncOrigin.LocalCrm;
                }
                break;

            case ConflictResolutionAction.KeepBoth:
                var project = await _dbContext.Projects.FindAsync(new object[] { conflict.ProjectId }, ct);
                var creator = await _dbContext.Employees.FirstOrDefaultAsync(ct);
                if (project != null && creator != null)
                {
                    var newItem = new WorkItem
                    {
                        Id = Guid.NewGuid(),
                        ProjectId = project.Id,
                        CreatedById = creator.Id,
                        Code = !string.IsNullOrEmpty(conflict.ExternalId) && conflict.ExternalId.StartsWith("ADO-", StringComparison.OrdinalIgnoreCase)
                            ? conflict.ExternalId
                            : $"ADO-{conflict.ExternalId}",
                        Title = conflict.ExternalTitle,
                        Description = conflict.ExternalDescription,
                        Status = WorkItemStatus.Todo,
                        ExternalSource = conflict.ExternalSource,
                        ExternalId = conflict.ExternalId,
                        LastSyncedContentHash = SyncContentHasher.ComputeHash(conflict.ExternalTitle, conflict.ExternalDescription, WorkItemStatus.Todo, WorkItemType.Task, null),
                        LastSyncOrigin = SyncOrigin.ExternalWebhook,
                        CreatedAt = DateTimeOffset.UtcNow,
                        UpdatedAt = DateTimeOffset.UtcNow
                    };
                    _dbContext.WorkItems.Add(newItem);
                }
                break;

            case ConflictResolutionAction.Skip:
                // No item created or updated; remote ID ignored
                break;
        }

        await _dbContext.SaveChangesAsync(ct);
        return new ConflictResolutionResultDto(conflict.Id, action, true, localItem?.Id, "Conflict resolved successfully");
    }

    private static double ComputeTitleSimilarity(string s1, string s2)
    {
        var n1 = s1.Trim().ToLowerInvariant();
        var n2 = s2.Trim().ToLowerInvariant();
        if (n1 == n2) return 1.0;

        int m = n1.Length;
        int n = n2.Length;
        if (m == 0 || n == 0) return 0.0;

        var d = new int[m + 1, n + 1];
        for (int i = 0; i <= m; i++) d[i, 0] = i;
        for (int j = 0; j <= n; j++) d[0, j] = j;

        for (int i = 1; i <= m; i++)
        {
            for (int j = 1; j <= n; j++)
            {
                int cost = (n1[i - 1] == n2[j - 1]) ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }

        int distance = d[m, n];
        int maxLen = Math.Max(m, n);
        return 1.0 - ((double)distance / maxLen);
    }

    private static WorkItemStatus ParseStatus(string? status) => (status?.ToLowerInvariant()) switch
    {
        "closed" or "done" => WorkItemStatus.Done,
        "active" or "in progress" => WorkItemStatus.InProgress,
        "resolved" or "in verify" => WorkItemStatus.InVerify,
        "new" or "to do" => WorkItemStatus.Todo,
        _ => WorkItemStatus.Todo
    };

    private static WorkItemType ParseType(string? type) => (type?.ToLowerInvariant()) switch
    {
        "bug" => WorkItemType.Bug,
        "user story" or "story" => WorkItemType.Story,
        _ => WorkItemType.Task
    };
}

/// <summary>
/// Daily planning cutoff job engine implementing IPlanningCutoffJob for test execution.
/// </summary>
public class TestPlanningCutoffJob : IPlanningCutoffJob
{
    private readonly TestCrmDbContext _dbContext;
    private readonly IEmailNotificationService _emailService;
    private readonly TimeProvider _timeProvider;

    public TestPlanningCutoffJob(
        TestCrmDbContext dbContext,
        IEmailNotificationService emailService,
        TimeProvider? timeProvider = null)
    {
        _dbContext = dbContext;
        _emailService = emailService;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Task<CutoffExecutionSummaryDto> ExecuteCutoffCheckAsync(CancellationToken ct = default)
        => ExecuteCutoffCheckAsync(projectId: null, ct);

    public async Task<CutoffExecutionSummaryDto> ExecuteCutoffCheckAsync(Guid? projectId, CancellationToken ct = default)
    {
        var startTime = _timeProvider.GetUtcNow();
        PlanningCutoffConfig? config = null;
        if (projectId.HasValue)
        {
            config = await _dbContext.PlanningCutoffConfigs.FirstOrDefaultAsync(c => c.ProjectId == projectId.Value, ct);
        }
        config ??= await _dbContext.PlanningCutoffConfigs.FirstOrDefaultAsync(c => c.ProjectId == null, ct)
            ?? await _dbContext.PlanningCutoffConfigs.FirstOrDefaultAsync(ct)
            ?? TestDataBuilder.CreateCutoffConfig(new TimeOnly(10, 0, 0));

        TimeZoneInfo timeZone;
        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(config.TimeZoneId);
        }
        catch
        {
            timeZone = TimeZoneInfo.Utc;
        }

        var localNow = TimeZoneInfo.ConvertTime(startTime, timeZone);
        var localTime = TimeOnly.FromTimeSpan(localNow.TimeOfDay);
        var localDate = DateOnly.FromDateTime(localNow.DateTime);

        // Check excluded days
        var excludedDays = ParseExcludedDays(config.ExcludedDaysOfWeekJson);
        if (excludedDays.Contains(localNow.DayOfWeek))
        {
            return new CutoffExecutionSummaryDto(
                "ExcludedDay", localDate, config.CutoffTimeLocal, timeZone.Id, 0, Array.Empty<string>(), 0, Array.Empty<string>());
        }

        // Check if before cutoff
        if (localTime < config.CutoffTimeLocal)
        {
            return new CutoffExecutionSummaryDto(
                "BeforeCutoff", localDate, config.CutoffTimeLocal, timeZone.Id, 0, Array.Empty<string>(), 0, Array.Empty<string>());
        }

        // Active employees (scoped to project members when projectId has members)
        List<Employee> employees;
        if (projectId.HasValue)
        {
            var memberIds = await _dbContext.ProjectMembers
                .Where(pm => pm.ProjectId == projectId.Value)
                .Select(pm => pm.EmployeeId)
                .Distinct()
                .ToListAsync(ct);

            employees = memberIds.Count > 0
                ? await _dbContext.Employees.Where(e => memberIds.Contains(e.Id)).ToListAsync(ct)
                : await _dbContext.Employees.ToListAsync(ct);
        }
        else
        {
            employees = await _dbContext.Employees.ToListAsync(ct);
        }
        var notifiedEmails = new List<string>();
        int sent = 0;

        foreach (var emp in employees)
        {
            // Did employee submit plan for today?
            bool hasPlan = await _dbContext.DailyPlans.AnyAsync(
                p => p.EmployeeId == emp.Id && p.PlanDate == localDate, ct);

            if (hasPlan) continue;

            // Was reminder already sent today?
            bool alreadySent = await _dbContext.DailyPlanReminderLogs.AnyAsync(
                l => l.EmployeeId == emp.Id && l.PlanDate == localDate, ct);

            if (alreadySent) continue;

            // Send reminder
            await _emailService.SendCutoffReminderEmailAsync(emp.Email, emp.FullName, localDate, config.CutoffTimeLocal, ct);

            _dbContext.DailyPlanReminderLogs.Add(new DailyPlanReminderLog
            {
                Id = Guid.NewGuid(),
                EmployeeId = emp.Id,
                PlanDate = localDate,
                ReminderSentAt = _timeProvider.GetUtcNow(),
                RecipientEmail = emp.Email,
                EmailDeliveryStatus = "Sent"
            });

            notifiedEmails.Add(emp.Email);
            sent++;
        }

        await _dbContext.SaveChangesAsync(ct);

        return new CutoffExecutionSummaryDto(
            "Completed", localDate, config.CutoffTimeLocal, timeZone.Id, sent, notifiedEmails, 0, Array.Empty<string>());
    }

    private static HashSet<DayOfWeek> ParseExcludedDays(string? json)
    {
        var set = new HashSet<DayOfWeek>();
        if (string.IsNullOrWhiteSpace(json)) return set;

        foreach (var day in CompanyCRM.Infrastructure.BackgroundJobs.DailyPlanCutoffReminderJob.ParseExcludedDays(json))
        {
            set.Add((DayOfWeek)day);
        }

        return set;
    }
}

/// <summary>
/// Legacy RBAC Evaluator facade preserved for backwards compatibility.
/// Delegates directly to production <see cref="RbacAuthorizationService.Instance"/>.
/// </summary>
[Obsolete("Use IRbacAuthorizationService / RbacAuthorizationService from CompanyCRM.Application.Services.")]
public static class RbacSecurityEvaluator
{
    public static bool CanAccessPublicDirectory(SecurityContext ctx) =>
        RbacAuthorizationService.Instance.CanAccessPublicDirectory(ctx);

    public static bool CanAccessReviewQueue(SecurityContext ctx) =>
        RbacAuthorizationService.Instance.CanAccessReviewQueue(ctx);

    public static bool CanManageCutoffConfig(SecurityContext ctx) =>
        RbacAuthorizationService.Instance.CanManageCutoffConfig(ctx);

    public static bool CanAccessManagementPortal(SecurityContext ctx) =>
        RbacAuthorizationService.Instance.CanAccessManagementPortal(ctx);

    public static int GetAuthorizationStatusCode(SecurityContext ctx, string endpoint, string httpMethod = "GET") =>
        RbacAuthorizationService.Instance.GetAuthorizationStatusCode(ctx, endpoint, httpMethod);

    [Obsolete("Use EmployeeSkill.SetProficiency domain method directly.")]
    public static void ValidateSkillProficiency(int proficiencyLevel)
    {
        var skill = new EmployeeSkill();
        skill.SetProficiency(proficiencyLevel);
    }
}

/// <summary>
/// Live timer concurrency manager ensuring single active timer exclusivity.
/// </summary>
public class TimerConcurrencyManager
{
    private TimeEntry? _activeTimer;

    public TimeEntry StartTimer(Guid employeeId, Guid workItemId, bool isBillable)
    {
        if (_activeTimer != null && _activeTimer.IsRunning)
        {
            StopTimer(_activeTimer);
        }

        _activeTimer = new TimeEntry
        {
            Id = Guid.NewGuid(),
            EmployeeId = employeeId,
            WorkItemId = workItemId,
            IsRunning = true,
            IsBillable = isBillable,
            StartedAt = DateTimeOffset.UtcNow
        };
        return _activeTimer;
    }

    public void StopTimer(TimeEntry timer)
    {
        timer.IsRunning = false;
        timer.EndedAt = DateTimeOffset.UtcNow;
        timer.DurationMinutes = Math.Max(1, (int)Math.Round((timer.EndedAt.Value - timer.StartedAt).TotalMinutes));
        if (_activeTimer == timer) _activeTimer = null;
    }
}
