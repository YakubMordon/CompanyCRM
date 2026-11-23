using CompanyCRM.Application.DTOs;
using CompanyCRM.Application.Interfaces;
using CompanyCRM.Domain.Entities;
using CompanyCRM.Domain.Enums;
using CompanyCRM.Infrastructure.ExternalSync;
using CompanyCRM.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CompanyCRM.Tests;

public class JiraSyncIntegrationTests : IDisposable
{
    private readonly CrmDbContext _dbContext;
    private readonly JiraSimulator _jiraSimulator;
    private readonly JiraIssueTrackerProvider _jiraProvider;
    private readonly AzureDevOpsSimulator _adoSimulator;
    private readonly AzureDevOpsIssueTrackerProvider _adoProvider;
    private readonly SyncOrchestratorService _syncOrchestrator;
    private readonly Project _project;
    private readonly Employee _employee;

    public JiraSyncIntegrationTests()
    {
        var options = new DbContextOptionsBuilder<CrmDbContext>()
            .UseInMemoryDatabase(databaseName: $"JiraSyncDb_{Guid.NewGuid()}")
            .Options;

        _dbContext = new CrmDbContext(options);
        _jiraSimulator = new JiraSimulator();
        _jiraProvider = new JiraIssueTrackerProvider(
            new HttpClient(),
            _jiraSimulator,
            NullLogger<JiraIssueTrackerProvider>.Instance);

        _adoSimulator = new AzureDevOpsSimulator();
        _adoProvider = new AzureDevOpsIssueTrackerProvider(
            new HttpClient(),
            _adoSimulator,
            NullLogger<AzureDevOpsIssueTrackerProvider>.Instance);

        var providers = new IIssueTrackerProvider[] { _adoProvider, _jiraProvider };
        _syncOrchestrator = new SyncOrchestratorService(
            _dbContext,
            providers,
            NullLogger<SyncOrchestratorService>.Instance);

        _employee = new Employee
        {
            Id = Guid.NewGuid(),
            Email = "jira.dev@companycrm.local",
            FullName = "Jira Test Engineer",
            Role = AppRole.Employee,
            SeniorityRank = SeniorityRank.Senior,
            Department = "Engineering",
            JobTitle = "Senior Software Engineer",
            HourlyCostRate = 85.0m,
            AnnualBaseSalary = 120000.0m,
            WeeklyCapacityHours = 40.0m
        };

        _project = new Project
        {
            Id = Guid.NewGuid(),
            Name = "Jira Connected Project",
            Code = "JCP",
            Description = "Test Jira integration",
            IsActive = true,
            IsSyncEnabled = true,
            ExternalSource = ExternalSourceType.Jira,
            ExternalOrgUrl = "https://integration.atlassian.net",
            ExternalProjectName = "JCP",
            IsSimulatedMode = true
        };

        _dbContext.Employees.Add(_employee);
        _dbContext.Projects.Add(_project);
        _dbContext.SaveChanges();
    }

    public void Dispose()
    {
        _dbContext.Database.EnsureDeleted();
        _dbContext.Dispose();
    }

    [Fact]
    public async Task PushWorkItemToExternalAsync_JiraProject_ShouldSynchronizeSuccessfully()
    {
        var workItem = new WorkItem
        {
            Id = Guid.NewGuid(),
            ProjectId = _project.Id,
            Project = _project,
            Code = "JCP-10",
            Title = "Implement Jira two-way sync adapter",
            Description = "Connect CompanyCRM to Jira REST API via simulated provider.",
            Status = WorkItemStatus.Backlog,
            Type = WorkItemType.Story,
            Priority = WorkItemPriority.High,
            ReviewStatus = ReviewStatus.Approved,
            CreatedById = _employee.Id,
            AssigneeId = _employee.Id,
            Assignee = _employee
        };

        _dbContext.WorkItems.Add(workItem);
        await _dbContext.SaveChangesAsync();

        var result = await _syncOrchestrator.PushWorkItemToExternalAsync(workItem.Id);

        result.Success.Should().BeTrue();
        result.ExternalId.Should().StartWith("JCP-");
        result.ExternalUrl.Should().Contain("integration.atlassian.net/browse/");
        result.Origin.Should().Be(SyncOrigin.LocalCrm);

        var updatedItem = await _dbContext.WorkItems.FindAsync(workItem.Id);
        updatedItem!.ExternalSource.Should().Be(ExternalSourceType.Jira);
        updatedItem.ExternalId.Should().Be(result.ExternalId);
        updatedItem.LastSyncedContentHash.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ProcessInboundWebhookAsync_JiraWebhook_ShouldApplyUpdate()
    {
        var workItem = new WorkItem
        {
            Id = Guid.NewGuid(),
            ProjectId = _project.Id,
            Project = _project,
            Code = "JCP-20",
            Title = "Initial WorkItem Title",
            Description = "Initial description",
            Status = WorkItemStatus.Todo,
            Type = WorkItemType.Task,
            Priority = WorkItemPriority.Medium,
            ReviewStatus = ReviewStatus.Approved,
            CreatedById = _employee.Id,
            ExternalSource = ExternalSourceType.Jira,
            ExternalId = "JCP-200",
            ExternalUrl = "https://integration.atlassian.net/browse/JCP-200",
            LastSyncedContentHash = "initialhash"
        };

        _dbContext.WorkItems.Add(workItem);
        await _dbContext.SaveChangesAsync();

        var updatedDto = new ExternalWorkItemDto(
            ExternalId: "JCP-200",
            ExternalUrl: "https://integration.atlassian.net/browse/JCP-200",
            Title: "Remotely Modified Title in Jira",
            Description: "Updated description from Jira board",
            Status: "In Progress",
            Type: "Task",
            AssigneeEmail: _employee.Email,
            LastModifiedAt: DateTimeOffset.UtcNow);

        var webhookJson = _jiraSimulator.GenerateWebhookPayload(updatedDto, "jira:issue_updated");
        var headers = new Dictionary<string, string>();

        var result = await _syncOrchestrator.ProcessInboundWebhookAsync(
            ExternalSourceType.Jira,
            webhookJson,
            headers);

        result.Status.Should().Be("Applied");
        result.IsEcho.Should().BeFalse();
        result.WorkItemId.Should().Be(workItem.Id);

        var refreshed = await _dbContext.WorkItems.FindAsync(workItem.Id);
        refreshed!.Title.Should().Be("Remotely Modified Title in Jira");
        refreshed.Description.Should().Be("Updated description from Jira board");
        refreshed.Status.Should().Be(WorkItemStatus.InProgress);
    }

    [Fact]
    public async Task ProcessInboundWebhookAsync_JiraEchoPayload_ShouldDetectEchoAndSkip()
    {
        var workItem = new WorkItem
        {
            Id = Guid.NewGuid(),
            ProjectId = _project.Id,
            Project = _project,
            Code = "JCP-30",
            Title = "Synchronized Title",
            Description = "Synchronized description",
            Status = WorkItemStatus.Todo,
            Type = WorkItemType.Task,
            Priority = WorkItemPriority.Medium,
            ReviewStatus = ReviewStatus.Approved,
            CreatedById = _employee.Id,
            AssigneeId = _employee.Id,
            Assignee = _employee,
            ExternalSource = ExternalSourceType.Jira,
            ExternalId = "JCP-300",
            ExternalUrl = "https://integration.atlassian.net/browse/JCP-300"
        };

        _dbContext.WorkItems.Add(workItem);
        await _dbContext.SaveChangesAsync();

        // Push first to establish sync hash
        var pushResult = await _syncOrchestrator.PushWorkItemToExternalAsync(workItem.Id);
        pushResult.Success.Should().BeTrue();

        var externalDto = new ExternalWorkItemDto(
            ExternalId: "JCP-300",
            ExternalUrl: "https://integration.atlassian.net/browse/JCP-300",
            Title: "Synchronized Title",
            Description: "Synchronized description",
            Status: "To Do",
            Type: "Task",
            AssigneeEmail: _employee.Email,
            LastModifiedAt: DateTimeOffset.UtcNow);

        var webhookJson = _jiraSimulator.GenerateWebhookPayload(externalDto, "jira:issue_updated");
        var headers = new Dictionary<string, string>();

        var webhookResult = await _syncOrchestrator.ProcessInboundWebhookAsync(
            ExternalSourceType.Jira,
            webhookJson,
            headers);

        webhookResult.IsEcho.Should().BeTrue();
        webhookResult.Status.Should().Be("IgnoredEcho");
    }
}
