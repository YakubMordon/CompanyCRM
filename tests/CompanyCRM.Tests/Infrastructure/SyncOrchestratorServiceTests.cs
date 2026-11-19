using CompanyCRM.Application.DTOs;
using CompanyCRM.Application.Services;
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

public class SyncOrchestratorServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<CrmDbContext> _options;
    private readonly AzureDevOpsSimulator _simulator;
    private readonly AzureDevOpsIssueTrackerProvider _provider;

    public SyncOrchestratorServiceTests()
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

    private async Task<(Project project, Employee employee, WorkItem workItem)> SeedBasicItemAsync(
        CrmDbContext context,
        WorkItemStatus status = WorkItemStatus.Todo,
        ReviewStatus reviewStatus = ReviewStatus.Approved)
    {
        await context.Database.EnsureCreatedAsync();

        var project = new Project
        {
            Code = "CRM",
            Name = "CompanyCRM",
            ExternalSource = ExternalSourceType.AzureDevOps,
            ExternalOrgUrl = "https://dev.azure.com/test-org",
            ExternalProjectName = "CRMProject",
            IsSyncEnabled = true,
            IsSimulatedMode = true
        };

        var employee = new Employee
        {
            FullName = "Jane Engineer",
            Email = "jane@company.com",
            Department = "Engineering",
            JobTitle = "Senior Dev"
        };

        context.Projects.Add(project);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var workItem = new WorkItem
        {
            ProjectId = project.Id,
            CreatedById = employee.Id,
            AssigneeId = employee.Id,
            Code = "CRM-101",
            Title = "Implement Two-Way Sync",
            Description = "Full bidirectional synchronization with Azure DevOps",
            Status = status,
            Type = WorkItemType.Task,
            ReviewStatus = reviewStatus
        };

        context.WorkItems.Add(workItem);
        await context.SaveChangesAsync();

        return (project, employee, workItem);
    }

    [Fact]
    public async Task PushWorkItem_ApprovedItem_CalculatesSha256Hash_AndSetsExternalFields()
    {
        using var context = CreateContext();
        var (_, _, workItem) = await SeedBasicItemAsync(context);
        var service = CreateService(context);

        var result = await service.PushWorkItemToExternalAsync(workItem.Id);

        result.Success.Should().BeTrue();
        result.ExternalId.Should().NotBeNullOrWhiteSpace();
        result.ContentHash.Should().HaveLength(64);
        result.Origin.Should().Be(SyncOrigin.LocalCrm);

        // Verify entity state in DB
        using var verifyContext = CreateContext();
        var updated = await verifyContext.WorkItems.FirstAsync(w => w.Id == workItem.Id);
        updated.ExternalId.Should().Be(result.ExternalId);
        updated.LastSyncedContentHash.Should().Be(result.ContentHash);
        updated.LastSyncOrigin.Should().Be(SyncOrigin.LocalCrm);
        updated.LastSyncedAt.Should().NotBeNull();

        // Verify WorkItemHistory audit
        var history = await verifyContext.WorkItemHistories.FirstOrDefaultAsync(h => h.WorkItemId == workItem.Id);
        history.Should().NotBeNull();
        history!.Action.Should().Be("SyncedToExternal");
    }

    [Theory]
    [InlineData(WorkItemStatus.PendingReview, ReviewStatus.Pending)]
    [InlineData(WorkItemStatus.ClarificationRequested, ReviewStatus.ClarificationNeeded)]
    [InlineData(WorkItemStatus.ClarificationRequested, ReviewStatus.NotRequired)]
    [InlineData(WorkItemStatus.Draft, ReviewStatus.NotRequired)]
    [InlineData(WorkItemStatus.Denied, ReviewStatus.Denied)]
    public async Task PushWorkItem_PendingOrDenied_ThrowsInvalidOperationException(WorkItemStatus status, ReviewStatus reviewStatus)
    {
        using var context = CreateContext();
        var (_, _, workItem) = await SeedBasicItemAsync(context, status, reviewStatus);
        var service = CreateService(context);

        var act = async () => await service.PushWorkItemToExternalAsync(workItem.Id);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Cannot push work item*");
    }

    [Fact]
    public async Task PushWorkItem_SyncDisabledProject_ReturnsFailureResult()
    {
        using var context = CreateContext();
        var (project, _, workItem) = await SeedBasicItemAsync(context);
        project.IsSyncEnabled = false;
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var result = await service.PushWorkItemToExternalAsync(workItem.Id);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("External synchronization is disabled");
    }

    [Fact]
    public async Task ProcessInboundWebhook_MatchingContentHash_ReturnsIgnoredEcho_AndNoDbWrite()
    {
        using var context = CreateContext();
        var (_, employee, workItem) = await SeedBasicItemAsync(context);
        var service = CreateService(context);

        // First push to establish ExternalId and LastSyncedContentHash
        var pushResult = await service.PushWorkItemToExternalAsync(workItem.Id);

        // Construct remote DTO with EXACT matching fields
        var incomingDto = new ExternalWorkItemDto(
            ExternalId: pushResult.ExternalId!,
            ExternalUrl: pushResult.ExternalUrl!,
            Title: workItem.Title,
            Description: workItem.Description,
            Status: AzureDevOpsIssueTrackerProvider.MapStatusToAdoState(workItem.Status),
            Type: AzureDevOpsIssueTrackerProvider.MapTypeToAdo(workItem.Type),
            AssigneeEmail: employee.Email,
            LastModifiedAt: DateTimeOffset.UtcNow);

        var rawPayload = _simulator.GenerateServiceHookPayload(incomingDto);

        // Track DB history count before webhook
        var historyCountBefore = await context.WorkItemHistories.CountAsync();

        // Process webhook
        var webhookResult = await service.ProcessInboundWebhookAsync(
            ExternalSourceType.AzureDevOps,
            rawPayload,
            new Dictionary<string, string>());

        webhookResult.Status.Should().Be("IgnoredEcho");
        webhookResult.IsEcho.Should().BeTrue();

        // Assert zero database updates or histories logged
        using var verifyContext = CreateContext();
        var historyCountAfter = await verifyContext.WorkItemHistories.CountAsync();
        historyCountAfter.Should().Be(historyCountBefore);
    }

    [Fact]
    public async Task ProcessInboundWebhook_RemoteStatusChanged_AppliesUpdate_AndRecordsExternalOrigin()
    {
        using var context = CreateContext();
        var (_, employee, workItem) = await SeedBasicItemAsync(context, WorkItemStatus.InProgress);
        var service = CreateService(context);

        var pushResult = await service.PushWorkItemToExternalAsync(workItem.Id);

        // Remote user changes status to "Closed" (Done in CRM)
        var incomingDto = new ExternalWorkItemDto(
            ExternalId: pushResult.ExternalId!,
            ExternalUrl: pushResult.ExternalUrl!,
            Title: workItem.Title,
            Description: workItem.Description,
            Status: "Closed",
            Type: "Task",
            AssigneeEmail: employee.Email,
            LastModifiedAt: DateTimeOffset.UtcNow);

        var rawPayload = _simulator.GenerateServiceHookPayload(incomingDto);

        var webhookResult = await service.ProcessInboundWebhookAsync(
            ExternalSourceType.AzureDevOps,
            rawPayload,
            new Dictionary<string, string>());

        webhookResult.Status.Should().Be("Applied");
        webhookResult.IsEcho.Should().BeFalse();

        // Assert entity state updated to Done and Origin is ExternalWebhook
        using var verifyContext = CreateContext();
        var updated = await verifyContext.WorkItems.FirstAsync(w => w.Id == workItem.Id);
        updated.Status.Should().Be(WorkItemStatus.Done);
        updated.LastSyncOrigin.Should().Be(SyncOrigin.ExternalWebhook);

        // Assert history record created
        var history = await verifyContext.WorkItemHistories
            .FirstOrDefaultAsync(h => h.WorkItemId == workItem.Id && h.Action == "SyncedFromExternal");
        history.Should().NotBeNull();
        history!.NewValue.Should().Be(WorkItemStatus.Done.ToString());
    }

    [Fact]
    public async Task ProcessInboundWebhook_AssigneeEmailMatches_AssignsLocalEmployee()
    {
        using var context = CreateContext();
        var (project, _, _) = await SeedBasicItemAsync(context);

        var newDev = new Employee
        {
            FullName = "Remote Collaborator",
            Email = "collaborator@company.com",
            Department = "Engineering",
            JobTitle = "Dev"
        };
        context.Employees.Add(newDev);

        var unassignedItem = new WorkItem
        {
            ProjectId = project.Id,
            CreatedById = newDev.Id,
            AssigneeId = null,
            Code = "CRM-305",
            Title = "Collaborator task",
            Status = WorkItemStatus.Todo,
            ReviewStatus = ReviewStatus.Approved,
            ExternalSource = ExternalSourceType.AzureDevOps,
            ExternalId = "ADO-EXT-305"
        };
        context.WorkItems.Add(unassignedItem);
        await context.SaveChangesAsync();

        var service = CreateService(context);

        var incomingDto = new ExternalWorkItemDto(
            ExternalId: "ADO-EXT-305",
            ExternalUrl: "https://dev.azure.com/org/proj/_workitems/edit/ADO-EXT-305",
            Title: "Collaborator task",
            Description: "Updated desc",
            Status: "Active",
            Type: "Task",
            AssigneeEmail: "collaborator@company.com",
            LastModifiedAt: DateTimeOffset.UtcNow);

        var payload = _simulator.GenerateServiceHookPayload(incomingDto);
        var result = await service.ProcessInboundWebhookAsync(ExternalSourceType.AzureDevOps, payload, new Dictionary<string, string>());

        result.Status.Should().Be("Applied");

        using var verifyContext = CreateContext();
        var updated = await verifyContext.WorkItems.FirstAsync(w => w.Id == unassignedItem.Id);
        updated.AssigneeId.Should().Be(newDev.Id);
    }

    [Fact]
    public async Task ProcessInboundWebhook_NovelItem_ImportsAsNewWorkItem()
    {
        using var context = CreateContext();
        await SeedBasicItemAsync(context);
        var service = CreateService(context);

        var novelDto = new ExternalWorkItemDto(
            ExternalId: "ADO-NOVEL-901",
            ExternalUrl: "https://dev.azure.com/org/proj/_workitems/edit/ADO-NOVEL-901",
            Title: "Completely novel feature created directly in ADO",
            Description: "Brand new story",
            Status: "Active",
            Type: "User Story",
            AssigneeEmail: "jane@company.com",
            LastModifiedAt: DateTimeOffset.UtcNow);

        var payload = _simulator.GenerateServiceHookPayload(novelDto);
        var result = await service.ProcessInboundWebhookAsync(ExternalSourceType.AzureDevOps, payload, new Dictionary<string, string>());

        result.Status.Should().Be("Applied");
        result.WorkItemId.Should().NotBeNull();

        using var verifyContext = CreateContext();
        var imported = await verifyContext.WorkItems.FirstAsync(w => w.Id == result.WorkItemId!.Value);
        imported.Code.Should().Be("EXT-ADO-NOVEL-901");
        imported.Title.Should().Be("Completely novel feature created directly in ADO");
        imported.Status.Should().Be(WorkItemStatus.InProgress);
        imported.Type.Should().Be(WorkItemType.Story);
        imported.LastSyncOrigin.Should().Be(SyncOrigin.ExternalWebhook);
    }
}
