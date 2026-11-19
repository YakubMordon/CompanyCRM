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
using CompanyCRM.Application.Interfaces;
using CompanyCRM.Application.DTOs;
using CompanyCRM.Tests.Common;

public class ReviewWorkflowTests
{
    private readonly TestCrmDbContext _dbContext;
    private readonly Mock<IEmailNotificationService> _emailServiceMock;
    private readonly Mock<ISyncOrchestratorService> _syncOrchestratorMock;
    private readonly Project _testProject;
    private readonly Employee _creator;
    private readonly Employee _reviewer;
    private readonly Employee _employeeOnly;

    public ReviewWorkflowTests()
    {
        _dbContext = TestDataBuilder.CreateInMemoryDbContext();
        _emailServiceMock = new Mock<IEmailNotificationService>();
        _syncOrchestratorMock = new Mock<ISyncOrchestratorService>();

        _testProject = TestDataBuilder.CreateProject();
        _creator = TestDataBuilder.CreateEmployee(email: "creator@corp.com", fullName: "Alice Dev", role: AppRole.Employee);
        _reviewer = TestDataBuilder.CreateEmployee(email: "reviewer@corp.com", fullName: "Bob Lead", role: AppRole.TeamLead);
        _employeeOnly = TestDataBuilder.CreateEmployee(email: "intern@corp.com", fullName: "Charlie Intern", role: AppRole.Employee);

        _dbContext.Projects.Add(_testProject);
        _dbContext.Employees.AddRange(_creator, _reviewer, _employeeOnly);
        _dbContext.SaveChanges();
    }

    private TestWorkItemReviewService CreateReviewService(Guid? callerId = null)
    {
        return new TestWorkItemReviewService(_dbContext, _emailServiceMock.Object, _syncOrchestratorMock.Object, callerId);
    }

    // =========================================================================
    // Tier 1: Feature Coverage (5 test cases)
    // =========================================================================

    [Fact]
    public async Task AssignReviewer_OnCreation_SetsPendingReviewStatus_AndDispatchesEmailWithDirectLink()
    {
        // Arrange
        var workItem = TestDataBuilder.CreateWorkItem(
            _testProject.Id,
            _creator.Id,
            code: "CRM-101",
            title: "Implement Reviewer Workflow",
            status: WorkItemStatus.Draft,
            reviewStatus: ReviewStatus.NotRequired);
        _dbContext.WorkItems.Add(workItem);
        await _dbContext.SaveChangesAsync();

        var service = CreateReviewService();

        // Act
        var result = await service.AssignReviewerAsync(workItem.Id, _reviewer.Id, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.ReviewStatus.Should().Be(ReviewStatus.Pending);
        result.Status.Should().Be(WorkItemStatus.PendingReview);
        result.ReviewerId.Should().Be(_reviewer.Id);

        var updatedItem = await _dbContext.WorkItems.FindAsync(workItem.Id);
        updatedItem!.ReviewStatus.Should().Be(ReviewStatus.Pending);
        updatedItem.Status.Should().Be(WorkItemStatus.PendingReview);
        updatedItem.ReviewerId.Should().Be(_reviewer.Id);

        // Verify automated email was dispatched to reviewer with work item metadata
        _emailServiceMock.Verify(x => x.SendReviewAssignedEmailAsync(
            _reviewer.Email,
            _reviewer.FullName,
            workItem.Code,
            workItem.Title,
            workItem.Id,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ApproveReview_WhenPending_TransitionsToTodo_AndInitiatesExternalSync()
    {
        // Arrange
        var workItem = TestDataBuilder.CreateWorkItem(
            _testProject.Id,
            _creator.Id,
            code: "CRM-102",
            title: "Add Audit History Timeline",
            status: WorkItemStatus.PendingReview,
            reviewStatus: ReviewStatus.Pending,
            reviewerId: _reviewer.Id);
        _dbContext.WorkItems.Add(workItem);
        await _dbContext.SaveChangesAsync();

        _syncOrchestratorMock
            .Setup(x => x.PushWorkItemToExternalAsync(workItem.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncResultDto(true, workItem.Id, "ADO-5001", "https://dev.azure.com/org/p/_workitems/edit/5001", "HASH", null, SyncOrigin.LocalCrm));

        var service = CreateReviewService(callerId: _reviewer.Id);
        const string approvalComment = "Architecture matches RFC-104. Approved for implementation.";

        // Act
        var result = await service.ApproveReviewAsync(workItem.Id, _reviewer.Id, approvalComment, CancellationToken.None);

        // Assert
        result.ReviewStatus.Should().Be(ReviewStatus.Approved);
        result.Status.Should().Be(WorkItemStatus.Todo);
        result.ReviewLatestComment.Should().Be(approvalComment);

        var dbItem = await _dbContext.WorkItems.FindAsync(workItem.Id);
        dbItem!.ReviewStatus.Should().Be(ReviewStatus.Approved);
        dbItem.Status.Should().Be(WorkItemStatus.Todo);

        // Verify outbound external sync was initiated
        _syncOrchestratorMock.Verify(x => x.PushWorkItemToExternalAsync(
            workItem.Id,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RequestClarification_WithValidComment_TransitionsToClarificationNeeded_AndNotifiesCreator()
    {
        // Arrange
        var workItem = TestDataBuilder.CreateWorkItem(
            _testProject.Id,
            _creator.Id,
            code: "CRM-103",
            title: "Refactor Time Tracking Timer",
            status: WorkItemStatus.PendingReview,
            reviewStatus: ReviewStatus.Pending,
            reviewerId: _reviewer.Id);
        _dbContext.WorkItems.Add(workItem);
        await _dbContext.SaveChangesAsync();

        var service = CreateReviewService(callerId: _reviewer.Id);
        const string note = "Please provide details on how active timers handle user session timeouts.";

        // Act
        var result = await service.RequestClarificationAsync(workItem.Id, _reviewer.Id, note, CancellationToken.None);

        // Assert
        result.ReviewStatus.Should().Be(ReviewStatus.ClarificationNeeded);
        result.Status.Should().Be(WorkItemStatus.ClarificationRequested);
        result.ReviewLatestComment.Should().Be(note);

        var dbItem = await _dbContext.WorkItems.FindAsync(workItem.Id);
        dbItem!.ReviewStatus.Should().Be(ReviewStatus.ClarificationNeeded);
        dbItem.Status.Should().Be(WorkItemStatus.ClarificationRequested);

        // Verify notification email sent to creator
        _emailServiceMock.Verify(x => x.SendReviewDecisionEmailAsync(
            _creator.Email,
            _creator.FullName,
            workItem.Code,
            workItem.Title,
            ReviewStatus.ClarificationNeeded,
            note,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DenyReview_WithValidComment_TransitionsToDenied_NotifiesCreator_AndPermanentlyBlocksSync()
    {
        // Arrange
        var workItem = TestDataBuilder.CreateWorkItem(
            _testProject.Id,
            _creator.Id,
            code: "CRM-104",
            title: "Custom Crypto Currency Billing Integration",
            status: WorkItemStatus.PendingReview,
            reviewStatus: ReviewStatus.Pending,
            reviewerId: _reviewer.Id);
        _dbContext.WorkItems.Add(workItem);
        await _dbContext.SaveChangesAsync();

        var service = CreateReviewService(callerId: _reviewer.Id);
        const string denialReason = "Out of compliance with internal payment provider policies.";

        // Act
        var result = await service.DenyReviewAsync(workItem.Id, _reviewer.Id, denialReason, CancellationToken.None);

        // Assert
        result.ReviewStatus.Should().Be(ReviewStatus.Denied);
        result.Status.Should().Be(WorkItemStatus.Denied);
        result.ReviewLatestComment.Should().Be(denialReason);

        var dbItem = await _dbContext.WorkItems.FindAsync(workItem.Id);
        dbItem!.ReviewStatus.Should().Be(ReviewStatus.Denied);
        dbItem.Status.Should().Be(WorkItemStatus.Denied);

        // Verify notification email sent to creator
        _emailServiceMock.Verify(x => x.SendReviewDecisionEmailAsync(
            _creator.Email,
            _creator.FullName,
            workItem.Code,
            workItem.Title,
            ReviewStatus.Denied,
            denialReason,
            It.IsAny<CancellationToken>()), Times.Once);

        // Verify outbound sync is NEVER invoked for denied item
        _syncOrchestratorMock.Verify(x => x.PushWorkItemToExternalAsync(
            It.IsAny<Guid>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetReviewQueue_ForReviewer_ReturnsOnlyPendingItemsForReviewerOrProject()
    {
        // Arrange
        var otherReviewer = TestDataBuilder.CreateEmployee(email: "other@corp.com", fullName: "Diana Lead", role: AppRole.TeamLead);
        _dbContext.Employees.Add(otherReviewer);

        var item1 = TestDataBuilder.CreateWorkItem(_testProject.Id, _creator.Id, "CRM-201", "Task 1", status: WorkItemStatus.PendingReview, reviewStatus: ReviewStatus.Pending, reviewerId: _reviewer.Id);
        var item2 = TestDataBuilder.CreateWorkItem(_testProject.Id, _creator.Id, "CRM-202", "Task 2", status: WorkItemStatus.PendingReview, reviewStatus: ReviewStatus.Pending, reviewerId: _reviewer.Id);
        var item3Other = TestDataBuilder.CreateWorkItem(_testProject.Id, _creator.Id, "CRM-203", "Task 3", status: WorkItemStatus.PendingReview, reviewStatus: ReviewStatus.Pending, reviewerId: otherReviewer.Id);
        var item4Approved = TestDataBuilder.CreateWorkItem(_testProject.Id, _creator.Id, "CRM-204", "Task 4", status: WorkItemStatus.Todo, reviewStatus: ReviewStatus.Approved, reviewerId: _reviewer.Id);
        var item5Denied = TestDataBuilder.CreateWorkItem(_testProject.Id, _creator.Id, "CRM-205", "Task 5", status: WorkItemStatus.Denied, reviewStatus: ReviewStatus.Denied, reviewerId: _reviewer.Id);

        _dbContext.WorkItems.AddRange(item1, item2, item3Other, item4Approved, item5Denied);
        await _dbContext.SaveChangesAsync();

        var service = CreateReviewService(callerId: _reviewer.Id);

        // Act
        var queue = await service.GetReviewQueueAsync(reviewerId: _reviewer.Id, projectId: _testProject.Id, CancellationToken.None);

        // Assert
        queue.Should().NotBeNull();
        queue.Should().HaveCount(2);
        queue.Select(q => q.WorkItemId).Should().Contain(new[] { item1.Id, item2.Id });
        queue.Select(q => q.WorkItemId).Should().NotContain(item3Other.Id);
        queue.Select(q => q.WorkItemId).Should().NotContain(item4Approved.Id);
        queue.Select(q => q.WorkItemId).Should().NotContain(item5Denied.Id);
        queue.All(q => q.ReviewStatus == ReviewStatus.Pending).Should().BeTrue();
    }

    // =========================================================================
    // Tier 2: Boundary & Corner Cases (5 test cases)
    // =========================================================================

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("1234")]
    [InlineData("  hi ")]
    public async Task RequestClarification_WithCommentShorterThan5Chars_ThrowsValidationException(string? invalidComment)
    {
        // Arrange
        var workItem = TestDataBuilder.CreateWorkItem(_testProject.Id, _creator.Id, "CRM-301", "Boundary Test", status: WorkItemStatus.PendingReview, reviewStatus: ReviewStatus.Pending, reviewerId: _reviewer.Id);
        _dbContext.WorkItems.Add(workItem);
        await _dbContext.SaveChangesAsync();

        var service = CreateReviewService(callerId: _reviewer.Id);

        // Act
        Func<Task> act = async () => await service.RequestClarificationAsync(workItem.Id, _reviewer.Id, invalidComment!, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*5 characters*");

        var dbItem = await _dbContext.WorkItems.FindAsync(workItem.Id);
        dbItem!.ReviewStatus.Should().Be(ReviewStatus.Pending);
        dbItem.Status.Should().Be(WorkItemStatus.PendingReview);

        _emailServiceMock.Verify(x => x.SendReviewDecisionEmailAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<ReviewStatus>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("    ")]
    [InlineData("no")]
    [InlineData("bad!")]
    public async Task DenyReview_WithCommentShorterThan5Chars_ThrowsValidationException(string? invalidReason)
    {
        // Arrange
        var workItem = TestDataBuilder.CreateWorkItem(_testProject.Id, _creator.Id, "CRM-302", "Boundary Deny", status: WorkItemStatus.PendingReview, reviewStatus: ReviewStatus.Pending, reviewerId: _reviewer.Id);
        _dbContext.WorkItems.Add(workItem);
        await _dbContext.SaveChangesAsync();

        var service = CreateReviewService(callerId: _reviewer.Id);

        // Act
        Func<Task> act = async () => await service.DenyReviewAsync(workItem.Id, _reviewer.Id, invalidReason!, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*5 characters*");

        var dbItem = await _dbContext.WorkItems.FindAsync(workItem.Id);
        dbItem!.ReviewStatus.Should().Be(ReviewStatus.Pending);

        _emailServiceMock.Verify(x => x.SendReviewDecisionEmailAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<ReviewStatus>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ApproveReview_WhenItemAlreadyApproved_ThrowsInvalidOperationException()
    {
        // Arrange
        var workItem = TestDataBuilder.CreateWorkItem(
            _testProject.Id,
            _creator.Id,
            "CRM-303",
            "Already Approved Item",
            status: WorkItemStatus.Todo,
            reviewStatus: ReviewStatus.Approved,
            reviewerId: _reviewer.Id);
        _dbContext.WorkItems.Add(workItem);
        await _dbContext.SaveChangesAsync();

        var service = CreateReviewService(callerId: _reviewer.Id);

        // Act
        Func<Task> act = async () => await service.ApproveReviewAsync(workItem.Id, _reviewer.Id, "Second approval attempt", CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already*approved*");

        _syncOrchestratorMock.Verify(x => x.PushWorkItemToExternalAsync(
            It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ApproveReview_WhenItemIsDenied_ThrowsInvalidOperationException_AndBlocksSync()
    {
        // Arrange
        var workItem = TestDataBuilder.CreateWorkItem(
            _testProject.Id,
            _creator.Id,
            "CRM-303-DENIED",
            "Denied Work Item",
            status: WorkItemStatus.Denied,
            reviewStatus: ReviewStatus.Denied,
            reviewerId: _reviewer.Id);
        _dbContext.WorkItems.Add(workItem);
        await _dbContext.SaveChangesAsync();

        var service = CreateReviewService(callerId: _reviewer.Id);

        // Act
        Func<Task> act = async () => await service.ApproveReviewAsync(workItem.Id, _reviewer.Id, "Attempt approve denied", CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Cannot approve a denied work item*");

        _syncOrchestratorMock.Verify(x => x.PushWorkItemToExternalAsync(
            It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReviewAction_WhenCallerHasEmployeeRole_ThrowsUnauthorizedAccessException()
    {
        // Arrange
        var workItem = TestDataBuilder.CreateWorkItem(
            _testProject.Id,
            _creator.Id,
            "CRM-304",
            "Auth Check Item",
            status: WorkItemStatus.PendingReview,
            reviewStatus: ReviewStatus.Pending,
            reviewerId: _reviewer.Id);
        _dbContext.WorkItems.Add(workItem);
        await _dbContext.SaveChangesAsync();

        // Caller is Charlie with AppRole.Employee
        var service = CreateReviewService(callerId: _employeeOnly.Id);

        // Act
        Func<Task> act = async () => await service.ApproveReviewAsync(workItem.Id, _employeeOnly.Id, "Unauthorized approve", CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("*Reviewer*role*");

        var dbItem = await _dbContext.WorkItems.FindAsync(workItem.Id);
        dbItem!.ReviewStatus.Should().Be(ReviewStatus.Pending);
    }

    [Theory]
    [InlineData(ReviewStatus.Pending, WorkItemStatus.PendingReview)]
    [InlineData(ReviewStatus.ClarificationNeeded, WorkItemStatus.ClarificationRequested)]
    [InlineData(ReviewStatus.Denied, WorkItemStatus.Denied)]
    public async Task KanbanTransition_WhenItemInPendingOrDeniedState_ThrowsInvalidOperationException(
        ReviewStatus reviewStatus,
        WorkItemStatus itemStatus)
    {
        // Arrange
        var workItem = TestDataBuilder.CreateWorkItem(
            _testProject.Id,
            _creator.Id,
            "CRM-305",
            "Kanban Gating Test",
            status: itemStatus,
            reviewStatus: reviewStatus,
            reviewerId: _reviewer.Id);
        _dbContext.WorkItems.Add(workItem);
        await _dbContext.SaveChangesAsync();

        var service = CreateReviewService();

        // Act
        Func<Task> act = async () => await service.UpdateWorkItemStatusAsync(workItem.Id, WorkItemStatus.InProgress, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*cannot transition Kanban columns until approved*");

        var dbItem = await _dbContext.WorkItems.FindAsync(workItem.Id);
        dbItem!.Status.Should().Be(itemStatus);
    }
}
