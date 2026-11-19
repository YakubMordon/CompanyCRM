using CompanyCRM.Application.DTOs;
using CompanyCRM.Domain.Entities;
using CompanyCRM.Domain.Enums;
using CompanyCRM.Infrastructure.ExternalSync;
using CompanyCRM.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CompanyCRM.Tests.Infrastructure;

public class DeduplicationConflictTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<CrmDbContext> _options;
    private readonly AzureDevOpsSimulator _simulator;
    private readonly AzureDevOpsIssueTrackerProvider _provider;

    public DeduplicationConflictTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<CrmDbContext>()
            .UseSqlite(_connection)
            .Options;

        _simulator = new AzureDevOpsSimulator();
        _provider = new AzureDevOpsIssueTrackerProvider(
            new HttpClient(),
            _simulator,
            NullLogger<AzureDevOpsIssueTrackerProvider>.Instance);
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    private CrmDbContext CreateContext() => new CrmDbContext(_options);

    private SyncOrchestratorService CreateService(CrmDbContext context)
    {
        return new SyncOrchestratorService(
            context,
            _provider,
            NullLogger<SyncOrchestratorService>.Instance);
    }

    [Fact]
    public void ComputeTitleSimilarity_IdenticalTitles_ReturnsOne()
    {
        var sim = SyncOrchestratorService.ComputeTitleSimilarity(
            "Implement OAuth2 Authentication Flow",
            "implement oauth2 authentication flow");

        sim.Should().Be(1.0);
    }

    [Fact]
    public void ComputeTitleSimilarity_ReorderedWords_ScoresAboveThreshold()
    {
        var sim = SyncOrchestratorService.ComputeTitleSimilarity(
            "Invoice PDF Generator",
            "PDF Generator Invoice");

        sim.Should().BeGreaterOrEqualTo(0.85);
    }

    [Fact]
    public void ComputeTitleSimilarity_CompletelyDifferent_ScoresLow()
    {
        var sim = SyncOrchestratorService.ComputeTitleSimilarity(
            "Fix database locking timeout in SQLite WAL mode",
            "Redesign navigation header color theme");

        sim.Should().BeLessThan(0.3);
    }

    [Fact]
    public async Task InboundWebhook_UnlinkedItemWithHighSimilarity_CreatesSyncConflict()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();

        var project = new Project
        {
            Code = "CRM",
            Name = "CompanyCRM",
            ExternalSource = ExternalSourceType.AzureDevOps,
            IsSyncEnabled = true
        };
        var employee = new Employee { FullName = "Developer", Email = "dev@company.com", Department = "Eng", JobTitle = "Dev" };
        context.Projects.Add(project);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        // Local unlinked work item
        var localItem = new WorkItem
        {
            ProjectId = project.Id,
            CreatedById = employee.Id,
            Code = "CRM-401",
            Title = "Refactor user profile avatar upload",
            Description = "Support compression for avatars",
            Status = WorkItemStatus.Todo,
            ExternalId = null
        };
        context.WorkItems.Add(localItem);
        await context.SaveChangesAsync();

        var service = CreateService(context);

        // Remote item with high similarity title
        var remoteDto = new ExternalWorkItemDto(
            ExternalId: "ADO-EXT-401",
            ExternalUrl: "https://dev.azure.com/org/proj/_workitems/edit/ADO-EXT-401",
            Title: "Refactor user profile avatar upload",
            Description: "Remote description",
            Status: "Active",
            Type: "Task",
            AssigneeEmail: "dev@company.com",
            LastModifiedAt: DateTimeOffset.UtcNow);

        var payload = _simulator.GenerateServiceHookPayload(remoteDto);
        var result = await service.ProcessInboundWebhookAsync(ExternalSourceType.AzureDevOps, payload, new Dictionary<string, string>());

        result.Status.Should().Be("ConflictDetected");
        result.Conflict.Should().NotBeNull();
        result.Conflict!.SimilarityScore.Should().BeGreaterOrEqualTo(0.85);

        using var verifyContext = CreateContext();
        var conflictInDb = await verifyContext.SyncConflicts.FirstOrDefaultAsync(c => c.ExternalId == "ADO-EXT-401");
        conflictInDb.Should().NotBeNull();
        conflictInDb!.Status.Should().Be(ConflictStatus.Pending);
        conflictInDb.LocalWorkItemId.Should().Be(localItem.Id);
    }

    [Fact]
    public async Task ResolveConflict_Merge_LinksLocalItemToExternalId_AndMarksResolved()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();

        var project = new Project { Code = "CRM", Name = "CompanyCRM", ExternalSource = ExternalSourceType.AzureDevOps, IsSyncEnabled = true };
        var employee = new Employee { FullName = "Dev", Email = "dev@company.com", Department = "Eng", JobTitle = "Dev" };
        context.Projects.Add(project);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var localItem = new WorkItem
        {
            ProjectId = project.Id,
            CreatedById = employee.Id,
            Code = "CRM-501",
            Title = "Merge Candidate Item",
            Status = WorkItemStatus.InProgress
        };
        context.WorkItems.Add(localItem);
        await context.SaveChangesAsync();

        var conflict = new SyncConflict
        {
            ProjectId = project.Id,
            LocalWorkItemId = localItem.Id,
            ExternalSource = ExternalSourceType.AzureDevOps,
            ExternalId = "ADO-9901",
            ExternalTitle = "Merge Candidate Item",
            ExternalStatus = "Active",
            Status = ConflictStatus.Pending,
            ConflictType = SyncConflictType.DuplicateCandidate
        };
        context.SyncConflicts.Add(conflict);
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var resolveResult = await service.ResolveConflictAsync(conflict.Id, ConflictResolutionAction.Merge);

        resolveResult.Resolved.Should().BeTrue();
        resolveResult.Action.Should().Be(ConflictResolutionAction.Merge);

        using var verifyContext = CreateContext();
        var resolvedConflict = await verifyContext.SyncConflicts.FirstAsync(c => c.Id == conflict.Id);
        resolvedConflict.Status.Should().Be(ConflictStatus.Resolved);
        resolvedConflict.ResolutionAction.Should().Be(ConflictResolutionAction.Merge);

        var updatedLocalItem = await verifyContext.WorkItems.FirstAsync(w => w.Id == localItem.Id);
        updatedLocalItem.ExternalId.Should().Be("ADO-9901");
        updatedLocalItem.LastSyncOrigin.Should().Be(SyncOrigin.LocalCrm);
    }

    [Fact]
    public async Task ResolveConflict_KeepBoth_CreatesNewWorkItem_AndMarksResolved()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();

        var project = new Project { Code = "CRM", Name = "CompanyCRM", ExternalSource = ExternalSourceType.AzureDevOps, IsSyncEnabled = true };
        var employee = new Employee { FullName = "Dev", Email = "dev@company.com", Department = "Eng", JobTitle = "Dev" };
        context.Projects.Add(project);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var localItem = new WorkItem
        {
            ProjectId = project.Id,
            CreatedById = employee.Id,
            Code = "CRM-601",
            Title = "Existing Local Task",
            Status = WorkItemStatus.Todo
        };
        context.WorkItems.Add(localItem);
        await context.SaveChangesAsync();

        var conflict = new SyncConflict
        {
            ProjectId = project.Id,
            LocalWorkItemId = localItem.Id,
            ExternalSource = ExternalSourceType.AzureDevOps,
            ExternalId = "ADO-9902",
            ExternalTitle = "Parallel Remote Task",
            ExternalDescription = "Created by partner team",
            ExternalStatus = "Active",
            Status = ConflictStatus.Pending,
            ConflictType = SyncConflictType.DuplicateCandidate
        };
        context.SyncConflicts.Add(conflict);
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var resolveResult = await service.ResolveConflictAsync(conflict.Id, ConflictResolutionAction.KeepBoth);

        resolveResult.Resolved.Should().BeTrue();

        using var verifyContext = CreateContext();
        var resolvedConflict = await verifyContext.SyncConflicts.FirstAsync(c => c.Id == conflict.Id);
        resolvedConflict.Status.Should().Be(ConflictStatus.Resolved);

        // Local item remains unchanged and unlinked
        var originalLocal = await verifyContext.WorkItems.FirstAsync(w => w.Id == localItem.Id);
        originalLocal.ExternalId.Should().BeNull();

        // A new work item has been imported
        var newImported = await verifyContext.WorkItems.FirstOrDefaultAsync(w => w.ExternalId == "ADO-9902");
        newImported.Should().NotBeNull();
        newImported!.Title.Should().Be("Parallel Remote Task");
        newImported.Code.Should().Be("ADO-9902");
    }

    [Fact]
    public async Task ResolveConflict_KeepBoth_WithNumericExternalId_PrefixesAdoOnce()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();

        var project = new Project { Code = "CRM", Name = "CompanyCRM", ExternalSource = ExternalSourceType.AzureDevOps, IsSyncEnabled = true };
        var employee = new Employee { FullName = "Dev", Email = "dev@company.com", Department = "Eng", JobTitle = "Dev" };
        context.Projects.Add(project);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var localItem = new WorkItem
        {
            ProjectId = project.Id,
            CreatedById = employee.Id,
            Code = "CRM-602",
            Title = "Existing Local Task 2",
            Status = WorkItemStatus.Todo
        };
        context.WorkItems.Add(localItem);
        await context.SaveChangesAsync();

        var conflict = new SyncConflict
        {
            ProjectId = project.Id,
            LocalWorkItemId = localItem.Id,
            ExternalSource = ExternalSourceType.AzureDevOps,
            ExternalId = "5002",
            ExternalTitle = "Parallel Remote Task 2",
            ExternalDescription = "Created by partner team",
            ExternalStatus = "Active",
            Status = ConflictStatus.Pending,
            ConflictType = SyncConflictType.DuplicateCandidate
        };
        context.SyncConflicts.Add(conflict);
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var resolveResult = await service.ResolveConflictAsync(conflict.Id, ConflictResolutionAction.KeepBoth);

        resolveResult.Resolved.Should().BeTrue();

        using var verifyContext = CreateContext();
        var newImported = await verifyContext.WorkItems.FirstOrDefaultAsync(w => w.ExternalId == "5002");
        newImported.Should().NotBeNull();
        newImported!.Code.Should().Be("ADO-5002");
    }

    [Fact]
    public async Task ResolveConflict_Skip_MarksResolvedWithoutCreatingItem()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();

        var project = new Project { Code = "CRM", Name = "CompanyCRM", ExternalSource = ExternalSourceType.AzureDevOps, IsSyncEnabled = true };
        var employee = new Employee { FullName = "Dev", Email = "dev@company.com", Department = "Eng", JobTitle = "Dev" };
        context.Projects.Add(project);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var localItem = new WorkItem
        {
            ProjectId = project.Id,
            CreatedById = employee.Id,
            Code = "CRM-701",
            Title = "Local Task To Keep Solo"
        };
        context.WorkItems.Add(localItem);
        await context.SaveChangesAsync();

        var conflict = new SyncConflict
        {
            ProjectId = project.Id,
            LocalWorkItemId = localItem.Id,
            ExternalSource = ExternalSourceType.AzureDevOps,
            ExternalId = "ADO-9903",
            ExternalTitle = "Irrelevant Remote Item",
            ExternalStatus = "New",
            Status = ConflictStatus.Pending,
            ConflictType = SyncConflictType.DuplicateCandidate
        };
        context.SyncConflicts.Add(conflict);
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var resolveResult = await service.ResolveConflictAsync(conflict.Id, ConflictResolutionAction.Skip);

        resolveResult.Resolved.Should().BeTrue();

        using var verifyContext = CreateContext();
        var resolvedConflict = await verifyContext.SyncConflicts.FirstAsync(c => c.Id == conflict.Id);
        resolvedConflict.Status.Should().Be(ConflictStatus.Resolved);
        resolvedConflict.ResolutionAction.Should().Be(ConflictResolutionAction.Skip);

        // No new work item created
        var imported = await verifyContext.WorkItems.FirstOrDefaultAsync(w => w.ExternalId == "ADO-9903");
        imported.Should().BeNull();

        // Subsequent webhook for ADO-9903 returns "Skipped"
        var remoteDto = new ExternalWorkItemDto(
            ExternalId: "ADO-9903",
            ExternalUrl: "https://dev.azure.com/org/proj/_workitems/edit/ADO-9903",
            Title: "Irrelevant Remote Item",
            Description: null,
            Status: "New",
            Type: "Task",
            AssigneeEmail: null,
            LastModifiedAt: DateTimeOffset.UtcNow);

        var payload = _simulator.GenerateServiceHookPayload(remoteDto);
        var webhookResult = await service.ProcessInboundWebhookAsync(ExternalSourceType.AzureDevOps, payload, new Dictionary<string, string>());
        webhookResult.Status.Should().Be("Skipped");
        resolveResult.ResolvedWorkItemId.Should().BeNull();
    }

    [Fact]
    public async Task GetPendingConflicts_UnlinkedConflict_ReturnsNullLocalCodeAndTitle_NotSyntheticFallback()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();

        var project = new Project { Code = "CRM", Name = "CompanyCRM", ExternalSource = ExternalSourceType.AzureDevOps, IsSyncEnabled = true };
        context.Projects.Add(project);
        await context.SaveChangesAsync();

        context.SyncConflicts.Add(new SyncConflict
        {
            ProjectId = project.Id,
            LocalWorkItemId = null,
            ExternalSource = ExternalSourceType.AzureDevOps,
            ExternalId = "ADO-8801",
            ExternalTitle = "Orphaned remote task",
            Status = ConflictStatus.Pending,
            ConflictType = SyncConflictType.DuplicateCandidate
        });
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var pending = await service.GetPendingConflictsAsync(project.Id);

        pending.Should().ContainSingle();
        pending[0].LocalWorkItemId.Should().BeNull();
        pending[0].LocalWorkItemCode.Should().BeNull();
        pending[0].LocalTitle.Should().BeNull();
    }

    [Fact]
    public async Task ResolveConflict_MergeWhenUnlinked_ThrowsInvalidOperationException_AndLeavesConflictPending()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();

        var project = new Project { Code = "CRM", Name = "CompanyCRM", ExternalSource = ExternalSourceType.AzureDevOps, IsSyncEnabled = true };
        context.Projects.Add(project);
        await context.SaveChangesAsync();

        var conflict = new SyncConflict
        {
            ProjectId = project.Id,
            LocalWorkItemId = null,
            ExternalSource = ExternalSourceType.AzureDevOps,
            ExternalId = "ADO-8802",
            ExternalTitle = "Unlinked merge attempt",
            Status = ConflictStatus.Pending,
            ConflictType = SyncConflictType.DuplicateCandidate
        };
        context.SyncConflicts.Add(conflict);
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var act = async () => await service.ResolveConflictAsync(conflict.Id, ConflictResolutionAction.Merge);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*no linked local work item*");

        conflict.Status.Should().Be(ConflictStatus.Pending);
        using var verifyContext = CreateContext();
        var dbConflict = await verifyContext.SyncConflicts.FirstAsync(c => c.Id == conflict.Id);
        dbConflict.Status.Should().Be(ConflictStatus.Pending);
    }

    [Fact]
    public async Task ResolveConflict_KeepBoth_ReturnsNewWorkItemId_NotLocalItemId()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();

        var project = new Project { Code = "CRM", Name = "CompanyCRM", ExternalSource = ExternalSourceType.AzureDevOps, IsSyncEnabled = true };
        var employee = new Employee { FullName = "Dev", Email = "dev@company.com", Department = "Eng", JobTitle = "Dev" };
        context.Projects.Add(project);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var localItem = new WorkItem
        {
            ProjectId = project.Id,
            CreatedById = employee.Id,
            Code = "CRM-801",
            Title = "Local Item Kept Separate",
            Status = WorkItemStatus.Todo
        };
        context.WorkItems.Add(localItem);
        await context.SaveChangesAsync();

        var conflict = new SyncConflict
        {
            ProjectId = project.Id,
            LocalWorkItemId = localItem.Id,
            ExternalSource = ExternalSourceType.AzureDevOps,
            ExternalId = "ADO-8803",
            ExternalTitle = "Remote Item Kept Separate",
            ExternalStatus = "Active",
            Status = ConflictStatus.Pending,
            ConflictType = SyncConflictType.DuplicateCandidate
        };
        context.SyncConflicts.Add(conflict);
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var resolveResult = await service.ResolveConflictAsync(conflict.Id, ConflictResolutionAction.KeepBoth);

        using var verifyContext = CreateContext();
        var newImported = await verifyContext.WorkItems.FirstAsync(w => w.ExternalId == "ADO-8803");
        resolveResult.ResolvedWorkItemId.Should().Be(newImported.Id);
        resolveResult.ResolvedWorkItemId.Should().NotBe(localItem.Id);
    }
}
