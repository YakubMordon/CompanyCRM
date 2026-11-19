namespace CompanyCRM.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using FluentAssertions;
using Moq;
using Microsoft.EntityFrameworkCore;
using CompanyCRM.Domain.Entities;
using CompanyCRM.Domain.Enums;
using CompanyCRM.Domain.ValueObjects;
using CompanyCRM.Application.Interfaces;
using CompanyCRM.Application.Services;
using CompanyCRM.Application.DTOs;
using CompanyCRM.Tests.Common;

public class SyncLoopPreventionTests
{
    private readonly TestCrmDbContext _dbContext;
    private readonly Mock<IIssueTrackerProvider> _adoProviderMock;
    private readonly Project _projectWithAdo;
    private readonly Employee _devAssignee;

    public SyncLoopPreventionTests()
    {
        _dbContext = TestDataBuilder.CreateInMemoryDbContext();
        _adoProviderMock = new Mock<IIssueTrackerProvider>();
        _adoProviderMock.Setup(p => p.SourceType).Returns(ExternalSourceType.AzureDevOps);

        _projectWithAdo = TestDataBuilder.CreateProject(
            code: "ADO",
            name: "Azure DevOps Linked Project",
            enableSync: true,
            isSimulatedMode: false,
            pat: "valid-pat-token-secret");

        _devAssignee = TestDataBuilder.CreateEmployee(email: "dev@companycorp.com", fullName: "Sam Engineer");

        _dbContext.Projects.Add(_projectWithAdo);
        _dbContext.Employees.Add(_devAssignee);
        _dbContext.SaveChanges();
    }

    private TestSyncOrchestratorService CreateSyncOrchestrator()
    {
        return new TestSyncOrchestratorService(_dbContext, _adoProviderMock.Object);
    }

    // =========================================================================
    // Tier 1: Feature Coverage (5 test cases)
    // =========================================================================

    [Fact]
    public async Task PushWorkItemToExternal_ComputesSha256Hash_AndRecordsExternalIdAndUrl()
    {
        // Arrange
        var item = TestDataBuilder.CreateWorkItem(
            _projectWithAdo.Id,
            _devAssignee.Id,
            code: "ADO-101",
            title: "Outbound Push Test",
            type: WorkItemType.Bug,
            status: WorkItemStatus.Todo,
            reviewStatus: ReviewStatus.Approved,
            assigneeId: _devAssignee.Id);
        item.Description = "Detailed bug reproduction steps.";
        _dbContext.WorkItems.Add(item);
        await _dbContext.SaveChangesAsync();

        _adoProviderMock
            .Setup(p => p.PushWorkItemAsync(It.IsAny<ProjectExternalConfig>(), It.IsAny<WorkItem>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExternalWorkItemDto(
                "1042",
                "https://dev.azure.com/companyorg/CompanyProject/_workitems/edit/1042",
                item.Title,
                item.Description,
                "Active",
                "Bug",
                _devAssignee.Email));

        var syncOrchestrator = CreateSyncOrchestrator();

        // Act
        var result = await syncOrchestrator.PushWorkItemToExternalAsync(item.Id, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.ExternalId.Should().Be("1042");

        var updated = await _dbContext.WorkItems.FindAsync(item.Id);
        updated!.ExternalId.Should().Be("1042");
        updated.ExternalUrl.Should().Be("https://dev.azure.com/companyorg/CompanyProject/_workitems/edit/1042");
        updated.LastSyncOrigin.Should().Be(SyncOrigin.LocalCrm);
        updated.LastSyncedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));

        // Canonical hash calculation verification
        var expectedHash = SyncContentHasher.ComputeHash(item.Title, item.Description, item.Status, item.Type, _devAssignee.Email);
        updated.LastSyncedContentHash.Should().Be(expectedHash);
        updated.LastSyncedContentHash.Should().HaveLength(64);
    }

    [Fact]
    public async Task ProcessInboundWebhook_WhenPayloadHashMatchesLastSyncedHash_TerminatesAsNoOpWithoutDbUpdates()
    {
        // Arrange
        const string initialTitle = "Synchronized Story";
        const string initialDesc = "Story details";
        const WorkItemStatus initialStatus = WorkItemStatus.InProgress;
        const WorkItemType initialType = WorkItemType.Story;
        var initialHash = SyncContentHasher.ComputeHash(initialTitle, initialDesc, initialStatus, initialType, _devAssignee.Email);

        var item = TestDataBuilder.CreateWorkItem(
            _projectWithAdo.Id,
            _devAssignee.Id,
            code: "ADO-102",
            title: initialTitle,
            type: initialType,
            status: initialStatus,
            reviewStatus: ReviewStatus.Approved,
            assigneeId: _devAssignee.Id,
            externalId: "1043",
            contentHash: initialHash);
        item.Description = initialDesc;
        _dbContext.WorkItems.Add(item);
        await _dbContext.SaveChangesAsync();

        var historyCountBefore = await _dbContext.WorkItemHistories.CountAsync();

        // Provider parses raw JSON into DTO matching the existing state exactly (the echo)
        _adoProviderMock
            .Setup(p => p.ParseWebhookPayloadAsync(It.IsAny<string>(), It.IsAny<IDictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExternalWorkItemDto(
                "1043",
                "https://dev.azure.com/sample/1043",
                initialTitle,
                initialDesc,
                "Active", // maps to InProgress
                "User Story",
                _devAssignee.Email));

        var syncOrchestrator = CreateSyncOrchestrator();
        var headers = new Dictionary<string, string> { { "X-Vssf-Sender", "AzureDevOps" } };

        // Act
        var result = await syncOrchestrator.ProcessInboundWebhookAsync(ExternalSourceType.AzureDevOps, "{}", headers, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Status.Should().Be("IgnoredEcho");
        result.IsEcho.Should().BeTrue();
        result.ExternalId.Should().Be("1043");

        // Verify zero database modifications occurred
        var reloaded = await _dbContext.WorkItems.FindAsync(item.Id);
        reloaded!.LastSyncedContentHash.Should().Be(initialHash);
        reloaded.LastSyncOrigin.Should().Be(SyncOrigin.LocalCrm);

        var historyCountAfter = await _dbContext.WorkItemHistories.CountAsync();
        historyCountAfter.Should().Be(historyCountBefore);

        // Outbound push must NOT be triggered
        _adoProviderMock.Verify(p => p.PushWorkItemAsync(
            It.IsAny<ProjectExternalConfig>(), It.IsAny<WorkItem>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessInboundWebhook_WhenRemoteFieldsModified_UpdatesLocalState_AndSetsExternalOrigin()
    {
        // Arrange
        const string initialTitle = "Original Title";
        const string initialDesc = "Original Desc";
        var oldHash = SyncContentHasher.ComputeHash(initialTitle, initialDesc, WorkItemStatus.Todo, WorkItemType.Task, _devAssignee.Email);

        var item = TestDataBuilder.CreateWorkItem(
            _projectWithAdo.Id,
            _devAssignee.Id,
            code: "ADO-103",
            title: initialTitle,
            type: WorkItemType.Task,
            status: WorkItemStatus.Todo,
            reviewStatus: ReviewStatus.Approved,
            assigneeId: _devAssignee.Id,
            externalId: "1044",
            contentHash: oldHash);
        item.Description = initialDesc;
        _dbContext.WorkItems.Add(item);
        await _dbContext.SaveChangesAsync();

        // Webhook arrives with modified title and status changed in ADO to "Closed" (Done)
        const string remoteModifiedTitle = "Original Title (Remote Hotfix)";
        const string remoteModifiedDesc = "Updated Remote Desc";

        _adoProviderMock
            .Setup(p => p.ParseWebhookPayloadAsync(It.IsAny<string>(), It.IsAny<IDictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExternalWorkItemDto(
                "1044",
                "https://dev.azure.com/sample/1044",
                remoteModifiedTitle,
                remoteModifiedDesc,
                "Closed", // maps to WorkItemStatus.Done
                "Task",
                _devAssignee.Email));

        var syncOrchestrator = CreateSyncOrchestrator();

        // Act
        var result = await syncOrchestrator.ProcessInboundWebhookAsync(
            ExternalSourceType.AzureDevOps,
            "{\"eventType\":\"workitem.updated\"}",
            new Dictionary<string, string>(),
            CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Status.Should().Be("Applied");
        result.IsEcho.Should().BeFalse();

        var updated = await _dbContext.WorkItems.FindAsync(item.Id);
        updated!.Title.Should().Be(remoteModifiedTitle);
        updated.Description.Should().Be(remoteModifiedDesc);
        updated.Status.Should().Be(WorkItemStatus.Done);
        updated.LastSyncOrigin.Should().Be(SyncOrigin.ExternalWebhook);
        updated.LastSyncedContentHash.Should().NotBe(oldHash);

        var expectedNewHash = SyncContentHasher.ComputeHash(remoteModifiedTitle, remoteModifiedDesc, WorkItemStatus.Done, WorkItemType.Task, _devAssignee.Email);
        updated.LastSyncedContentHash.Should().Be(expectedNewHash);
    }

    [Fact]
    public async Task RunReconciliation_WhenUnlinkedRemoteItemDiscovered_DetectsDuplicateCandidateBySimilarity()
    {
        // Arrange
        // Existing unlinked CRM item
        var localItem = TestDataBuilder.CreateWorkItem(
            _projectWithAdo.Id,
            _devAssignee.Id,
            code: "ADO-104",
            title: "Build Customer Invoice PDF Generator",
            type: WorkItemType.Story,
            status: WorkItemStatus.Todo,
            reviewStatus: ReviewStatus.Approved,
            externalId: null); // unlinked!
        _dbContext.WorkItems.Add(localItem);
        await _dbContext.SaveChangesAsync();

        // ADO returns an unlinked remote item with 95% matching title
        _adoProviderMock
            .Setup(p => p.FetchRemoteWorkItemsAsync(It.IsAny<ProjectExternalConfig>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ExternalWorkItemDto>
            {
                new(
                    "9988",
                    "https://dev.azure.com/sample/9988",
                    "Build Customer Invoicing PDF Generator",
                    "Invoicing generator task",
                    "New",
                    "User Story",
                    null)
            });

        var syncOrchestrator = CreateSyncOrchestrator();

        // Act
        var result = await syncOrchestrator.RunReconciliationAsync(_projectWithAdo.Id, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.ConflictsCount.Should().Be(1);

        var conflict = await _dbContext.SyncConflicts.FirstOrDefaultAsync(c => c.ExternalId == "9988");
        conflict.Should().NotBeNull();
        conflict!.ConflictType.Should().Be(SyncConflictType.DuplicateCandidate);
        conflict.Status.Should().Be(ConflictStatus.Pending);
        conflict.LocalWorkItemId.Should().Be(localItem.Id);
        conflict.SimilarityScore.Should().BeGreaterThanOrEqualTo(0.85);
    }

    [Theory]
    [InlineData(ConflictResolutionAction.Merge)]
    [InlineData(ConflictResolutionAction.KeepBoth)]
    [InlineData(ConflictResolutionAction.Skip)]
    public async Task ResolveConflict_ExecutesSpecifiedAction_Merge_KeepBoth_And_Skip(ConflictResolutionAction action)
    {
        // Arrange
        var localItem = TestDataBuilder.CreateWorkItem(
            _projectWithAdo.Id,
            _devAssignee.Id,
            code: "ADO-105",
            title: "Sync Conflict Reconciliation Task",
            externalId: null);
        _dbContext.WorkItems.Add(localItem);
        await _dbContext.SaveChangesAsync();

        var conflict = TestDataBuilder.CreateConflict(
            _projectWithAdo.Id,
            localItem.Id,
            externalId: "5544",
            externalTitle: "Sync Conflict Reconciliation Task Remote");
        _dbContext.SyncConflicts.Add(conflict);
        await _dbContext.SaveChangesAsync();

        var syncOrchestrator = CreateSyncOrchestrator();

        // Act
        var res = await syncOrchestrator.ResolveConflictAsync(conflict.Id, action, CancellationToken.None);

        // Assert
        res.Resolved.Should().BeTrue();
        res.Action.Should().Be(action);

        var updatedConflict = await _dbContext.SyncConflicts.FindAsync(conflict.Id);
        updatedConflict!.Status.Should().Be(ConflictStatus.Resolved);
        updatedConflict.ResolutionAction.Should().Be(action);

        var reloadedLocal = await _dbContext.WorkItems.FindAsync(localItem.Id);

        switch (action)
        {
            case ConflictResolutionAction.Merge:
                reloadedLocal!.ExternalId.Should().Be("5544");
                reloadedLocal.LastSyncedContentHash.Should().NotBeNullOrEmpty();
                break;

            case ConflictResolutionAction.KeepBoth:
                reloadedLocal!.ExternalId.Should().BeNull();
                var createdRemoteCopy = await _dbContext.WorkItems.FirstOrDefaultAsync(w => w.ExternalId == "5544");
                createdRemoteCopy.Should().NotBeNull();
                createdRemoteCopy!.Id.Should().NotBe(localItem.Id);
                createdRemoteCopy.Title.Should().Be(conflict.ExternalTitle);
                break;

            case ConflictResolutionAction.Skip:
                reloadedLocal!.ExternalId.Should().BeNull();
                var skippedCopy = await _dbContext.WorkItems.FirstOrDefaultAsync(w => w.ExternalId == "5544");
                skippedCopy.Should().BeNull();
                break;
        }
    }

    // =========================================================================
    // Tier 2: Boundary & Corner Cases (5 test cases)
    // =========================================================================

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n  ")]
    public void ComputeHash_WithNullOrWhitespaceDescription_NormalizesToEmptyString_Deterministically(string? description)
    {
        // Arrange
        const string title = "Standard Title";
        const WorkItemStatus status = WorkItemStatus.Todo;
        const WorkItemType type = WorkItemType.Task;
        const string email = "engineer@corp.com";

        // Act
        var hash = SyncContentHasher.ComputeHash(title, description, status, type, email);

        // Assert
        var expectedHash = SyncContentHasher.ComputeHash(title, string.Empty, status, type, email);
        hash.Should().Be(expectedHash);
        hash.Should().HaveLength(64);
        hash.Should().MatchRegex("^[A-F0-9]{64}$");
    }

    [Theory]
    [InlineData("developer@corp.com")]
    [InlineData("DEVELOPER@CORP.COM")]
    [InlineData("Developer@Corp.Com")]
    [InlineData("  developer@corp.com  ")]
    [InlineData("\tDeveloper@Corp.Com\n")]
    public void ComputeHash_WithMixedCaseAssigneeEmail_NormalizesCaseInsensitively(string? email)
    {
        // Arrange
        const string title = "Authentication Guard";
        const string desc = "Verify token lifespan";
        const WorkItemStatus status = WorkItemStatus.InProgress;
        const WorkItemType type = WorkItemType.Bug;

        // Act
        var hash = SyncContentHasher.ComputeHash(title, desc, status, type, email);

        // Assert
        var canonicalHash = SyncContentHasher.ComputeHash(title, desc, status, type, "developer@corp.com");
        hash.Should().Be(canonicalHash);
    }

    [Fact]
    public async Task ResolveConflict_WhenConflictAlreadyResolved_ThrowsInvalidOperationException()
    {
        // Arrange
        var conflict = TestDataBuilder.CreateConflict(
            _projectWithAdo.Id,
            localWorkItemId: null,
            externalId: "8877",
            status: ConflictStatus.Resolved);
        conflict.ResolutionAction = ConflictResolutionAction.Skip;
        conflict.ResolvedAt = DateTimeOffset.UtcNow.AddMinutes(-30);
        _dbContext.SyncConflicts.Add(conflict);
        await _dbContext.SaveChangesAsync();

        var syncOrchestrator = CreateSyncOrchestrator();

        // Act
        Func<Task> act = async () => await syncOrchestrator.ResolveConflictAsync(
            conflict.Id,
            ConflictResolutionAction.Merge,
            CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already resolved*");
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("secret-pat", true)]
    public async Task PushWorkItemToExternal_WhenPatIsMissingOrSimulated_FallsBackToDeterministicSandboxProvider(
        string? pat,
        bool isSimulated)
    {
        // Arrange
        var project = TestDataBuilder.CreateProject(
            code: "SIM",
            name: "Sandbox Fallback Project",
            enableSync: true,
            isSimulatedMode: isSimulated,
            pat: pat);
        _dbContext.Projects.Add(project);

        var item = TestDataBuilder.CreateWorkItem(
            project.Id,
            _devAssignee.Id,
            code: "SIM-501",
            title: "Simulated Sandbox Item",
            status: WorkItemStatus.Todo,
            reviewStatus: ReviewStatus.Approved);
        _dbContext.WorkItems.Add(item);
        await _dbContext.SaveChangesAsync();

        var syncOrchestrator = CreateSyncOrchestrator();

        // Act
        var result = await syncOrchestrator.PushWorkItemToExternalAsync(item.Id, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.ExternalId.Should().Be("ADO-SIM-SIM501");
        result.ExternalUrl.Should().Contain("simulated-org");

        var updated = await _dbContext.WorkItems.FindAsync(item.Id);
        updated!.ExternalId.Should().Be("ADO-SIM-SIM501");
        updated.LastSyncedContentHash.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ProcessInboundWebhook_WithUnknownExternalId_CreatesNewWorkItemOrFlagsConflict()
    {
        // Arrange
        const string unknownExternalId = "ADO-9999";
        const string novelTitle = "Completely New Remote Feature";

        _adoProviderMock
            .Setup(p => p.ParseWebhookPayloadAsync(It.IsAny<string>(), It.IsAny<IDictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExternalWorkItemDto(
                unknownExternalId,
                $"https://dev.azure.com/sample/{unknownExternalId}",
                novelTitle,
                "Discovered externally in ADO board",
                "To Do",
                "Task",
                null));

        var syncOrchestrator = CreateSyncOrchestrator();

        // Act
        var result = await syncOrchestrator.ProcessInboundWebhookAsync(
            ExternalSourceType.AzureDevOps,
            "{\"resource\":{\"id\":9999}}",
            new Dictionary<string, string>(),
            CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Status.Should().Be("Applied");
        result.ExternalId.Should().Be(unknownExternalId);

        var createdItem = await _dbContext.WorkItems.FirstOrDefaultAsync(w => w.ExternalId == unknownExternalId);
        createdItem.Should().NotBeNull();
        createdItem!.Title.Should().Be(novelTitle);
        createdItem.Status.Should().Be(WorkItemStatus.Todo);
        createdItem.LastSyncOrigin.Should().Be(SyncOrigin.ExternalWebhook);
        createdItem.LastSyncedContentHash.Should().NotBeNullOrEmpty();
    }
}
