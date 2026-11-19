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
using CompanyCRM.Application.DTOs;
using CompanyCRM.Application.Interfaces;
using CompanyCRM.Application.Services;
using CompanyCRM.Domain.Entities;
using CompanyCRM.Domain.Enums;
using CompanyCRM.Domain.ValueObjects;
using CompanyCRM.Tests.Common;

public class RealWorldApplicationScenarioTests
{
    // =========================================================================
    // Tier 3: Cross-Feature Combinations (Pairwise Coverage, 6 test cases)
    // =========================================================================

    [Fact]
    public async Task ReviewerApproval_TriggersOutboundAdoSync_WhenWorkItemApproved()
    {
        // Arrange: Review Workflow <--> Outbound ADO Sync
        using var dbContext = TestDataBuilder.CreateInMemoryDbContext();
        var project = TestDataBuilder.CreateProject(enableSync: true, isSimulatedMode: false, pat: "pat-secret");
        var creator = TestDataBuilder.CreateEmployee(email: "john@corp.com", fullName: "Dev John", role: AppRole.Employee);
        var reviewer = TestDataBuilder.CreateEmployee(email: "sarah@corp.com", fullName: "Lead Sarah", role: AppRole.TeamLead);

        dbContext.Projects.Add(project);
        dbContext.Employees.AddRange(creator, reviewer);
        await dbContext.SaveChangesAsync();

        var workItem = TestDataBuilder.CreateWorkItem(project.Id, creator.Id, "CRM-101", "Item to approve", status: WorkItemStatus.PendingReview, reviewStatus: ReviewStatus.Pending, reviewerId: reviewer.Id);
        dbContext.WorkItems.Add(workItem);
        await dbContext.SaveChangesAsync();

        var mockTracker = new Mock<IIssueTrackerProvider>();
        mockTracker.Setup(t => t.PushWorkItemAsync(It.IsAny<ProjectExternalConfig>(), It.IsAny<WorkItem>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExternalWorkItemDto("ADO-101", "https://dev.azure.com/sample-org/SampleProject/_workitems/edit/101", workItem.Title, workItem.Description, "To Do", "Task", null));

        var mockEmail = new Mock<IEmailNotificationService>();
        var syncOrchestrator = new TestSyncOrchestratorService(dbContext, mockTracker.Object);
        var reviewService = new TestWorkItemReviewService(dbContext, mockEmail.Object, syncOrchestrator, reviewer.Id);

        // Act
        var result = await reviewService.ApproveReviewAsync(workItem.Id, reviewer.Id, "LGTM!");

        // Assert
        result.ReviewStatus.Should().Be(ReviewStatus.Approved);
        result.Status.Should().Be(WorkItemStatus.Todo);

        var dbItem = await dbContext.WorkItems.FindAsync(workItem.Id);
        dbItem!.ExternalId.Should().Be("ADO-101");
        mockTracker.Verify(t => t.PushWorkItemAsync(
            It.IsAny<ProjectExternalConfig>(),
            It.Is<WorkItem>(w => w.Id == workItem.Id),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReviewerDenial_BlocksOutboundAdoSync_WhenWorkItemDenied()
    {
        // Arrange: Review Workflow <--> Sync Gating
        using var dbContext = TestDataBuilder.CreateInMemoryDbContext();
        var project = TestDataBuilder.CreateProject(enableSync: true);
        var creator = TestDataBuilder.CreateEmployee(email: "john@corp.com", fullName: "Dev John", role: AppRole.Employee);
        var reviewer = TestDataBuilder.CreateEmployee(email: "sarah@corp.com", fullName: "Lead Sarah", role: AppRole.TeamLead);

        dbContext.Projects.Add(project);
        dbContext.Employees.AddRange(creator, reviewer);
        await dbContext.SaveChangesAsync();

        var workItem = TestDataBuilder.CreateWorkItem(project.Id, creator.Id, "CRM-102", "Item to deny", status: WorkItemStatus.PendingReview, reviewStatus: ReviewStatus.Pending, reviewerId: reviewer.Id);
        dbContext.WorkItems.Add(workItem);
        await dbContext.SaveChangesAsync();

        var mockTracker = new Mock<IIssueTrackerProvider>();
        var mockEmail = new Mock<IEmailNotificationService>();
        var syncOrchestrator = new TestSyncOrchestratorService(dbContext, mockTracker.Object);
        var reviewService = new TestWorkItemReviewService(dbContext, mockEmail.Object, syncOrchestrator, reviewer.Id);

        // Act
        var result = await reviewService.DenyReviewAsync(workItem.Id, reviewer.Id, "Out of scope for current sprint.");

        // Assert
        result.ReviewStatus.Should().Be(ReviewStatus.Denied);
        result.Status.Should().Be(WorkItemStatus.Denied);

        var dbItem = await dbContext.WorkItems.FindAsync(workItem.Id);
        dbItem!.ExternalId.Should().BeNull();
        mockTracker.Verify(t => t.PushWorkItemAsync(
            It.IsAny<ProjectExternalConfig>(),
            It.IsAny<WorkItem>(),
            It.IsAny<CancellationToken>()), Times.Never);

        mockEmail.Verify(e => e.SendReviewDecisionEmailAsync(
            creator.Email, creator.FullName, workItem.Code, workItem.Title, ReviewStatus.Denied, "Out of scope for current sprint.", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void TimeTracking_SeparatesBillableVsNonBillableEntries_IntoRevenueAndCostCorrectly()
    {
        // Arrange: Time Tracking (IsBillable) <--> Financial Calculations
        var entries = new List<(decimal DurationMinutes, decimal HourlyRate, bool IsBillable)>
        {
            // 5 hours billable @ $180/hr = $900.00
            (300m, 180.00m, true),
            // 3 hours non-billable @ $180/hr = $0.00
            (180m, 180.00m, false)
        };

        var costEntries = new List<(decimal DurationMinutes, decimal HourlyCostRate)>
        {
            // 8 hours total labor cost @ $60/hr = $480.00
            (300m, 60.00m),
            (180m, 60.00m)
        };

        // Act
        var revenue = FinancialCalculator.CalculateProjectRevenue(entries);
        var cost = FinancialCalculator.CalculateProjectInternalCost(costEntries);
        var (profit, margin) = FinancialCalculator.CalculateProfitability(revenue, cost);

        // Assert
        // Revenue: 5h * $180 = $900.00
        revenue.Should().Be(900.00m);
        // Cost: 8h * $60 = $480.00
        cost.Should().Be(480.00m);
        // Profit: $900 - $480 = $420.00
        profit.Should().Be(420.00m);
        // Margin: (420 / 900) * 100 = 46.67%
        margin.Should().Be(46.67m);
    }

    [Fact]
    public async Task DailyPlanSubmission_BeforeCutoffTime_SuppressesCutoffReminderEmail()
    {
        // Arrange: Daily Planning <--> Cutoff Reminder Job
        using var dbContext = TestDataBuilder.CreateInMemoryDbContext();
        var timeProvider = new TestTimeProvider(DateTimeOffset.Parse("2026-11-16T10:15:00Z")); // 10:15 UTC

        var config = TestDataBuilder.CreateCutoffConfig(new TimeOnly(10, 0, 0), "UTC"); // 10:00 AM cutoff
        dbContext.PlanningCutoffConfigs.Add(config);

        var alice = TestDataBuilder.CreateEmployee(email: "alice@company.com", fullName: "Alice Developer");
        dbContext.Employees.Add(alice);

        // Alice submitted plan at 09:30 AM
        var submittedPlan = new DailyPlan
        {
            Id = Guid.NewGuid(),
            EmployeeId = alice.Id,
            PlanDate = new DateOnly(2026, 11, 16),
            SubmittedAt = DateTimeOffset.Parse("2026-11-16T09:30:00Z"),
            IsSubmittedOnTime = true
        };
        dbContext.DailyPlans.Add(submittedPlan);
        await dbContext.SaveChangesAsync();

        var mockEmail = new Mock<IEmailNotificationService>();
        var planningJob = new TestPlanningCutoffJob(dbContext, mockEmail.Object, timeProvider);

        // Act: Job runs at 10:15 AM
        var summary = await planningJob.ExecuteCutoffCheckAsync(CancellationToken.None);

        // Assert
        summary.RemindersSent.Should().Be(0);
        mockEmail.Verify(e => e.SendCutoffReminderEmailAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<TimeOnly>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void DailyPlanSubmission_AfterCutoffTime_SetsIsSubmittedOnTimeFalse()
    {
        // Arrange: Daily Planning <--> Cutoff Time Engine
        var config = TestDataBuilder.CreateCutoffConfig(new TimeOnly(10, 0, 0), "UTC");
        var planDate = new DateOnly(2026, 11, 16);
        var dailyPlan = new DailyPlan
        {
            Id = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid(),
            PlanDate = planDate
        };
        var actualSubmissionTime = DateTimeOffset.Parse("2026-11-16T10:15:00Z"); // 15 mins late

        // Act
        dailyPlan.EvaluateSubmissionTiming(config, actualSubmissionTime);

        // Assert
        dailyPlan.IsSubmittedOnTime.Should().BeFalse();
        dailyPlan.SubmittedAt.Should().Be(actualSubmissionTime);
    }

    [Fact]
    public async Task AdoSyncWebhook_EchoPayload_IgnoresWorkItemHistoryWhileExternalEditAppendsHistory()
    {
        // Arrange: ADO Webhook Inbound <--> WorkItemHistory Audit Log
        using var dbContext = TestDataBuilder.CreateInMemoryDbContext();
        var project = TestDataBuilder.CreateProject(enableSync: true);
        var dev = TestDataBuilder.CreateEmployee(email: "alice@company.com", fullName: "Dev Alice");
        dbContext.Projects.Add(project);
        dbContext.Employees.Add(dev);

        var workItem = TestDataBuilder.CreateWorkItem(project.Id, dev.Id, "CRM-500", "Refactor Database Index", status: WorkItemStatus.Todo);
        workItem.ExternalId = "ADO-500";
        var existingHash = SyncContentHasher.ComputeHash(workItem.Title, workItem.Description, workItem.Status, workItem.Type, dev.Email);
        workItem.LastSyncedContentHash = existingHash;
        dbContext.WorkItems.Add(workItem);
        await dbContext.SaveChangesAsync();

        var mockTracker = new Mock<IIssueTrackerProvider>();
        var syncOrchestrator = new TestSyncOrchestratorService(dbContext, mockTracker.Object);

        // Act 1: Inbound Webhook is an Echo (same hash)
        mockTracker.Setup(t => t.ParseWebhookPayloadAsync(It.IsAny<string>(), It.IsAny<IDictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExternalWorkItemDto(
                "ADO-500", "https://dev.azure.com/sample/500", workItem.Title, workItem.Description, "To Do", "Task", dev.Email));

        var echoResult = await syncOrchestrator.ProcessInboundWebhookAsync(
            ExternalSourceType.AzureDevOps, "{}", new Dictionary<string, string>(), CancellationToken.None);

        // Assert 1: Echo ignored, zero history logged
        echoResult.Status.Should().Be("IgnoredEcho");
        echoResult.IsEcho.Should().BeTrue();
        (await dbContext.WorkItemHistories.CountAsync()).Should().Be(0);

        // Act 2: Inbound Webhook is Genuine External Edit (Status changed to Closed / Done)
        mockTracker.Setup(t => t.ParseWebhookPayloadAsync(It.IsAny<string>(), It.IsAny<IDictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExternalWorkItemDto(
                "ADO-500", "https://dev.azure.com/sample/500", workItem.Title, workItem.Description, "Closed", "Task", dev.Email));

        var updateResult = await syncOrchestrator.ProcessInboundWebhookAsync(
            ExternalSourceType.AzureDevOps, "{\"update\":true}", new Dictionary<string, string>(), CancellationToken.None);

        // Assert 2: Update applied, history record appended
        updateResult.Status.Should().Be("Applied");
        updateResult.IsEcho.Should().BeFalse();
        (await dbContext.WorkItemHistories.CountAsync()).Should().Be(1);

        var history = await dbContext.WorkItemHistories.FirstAsync();
        history.Action.Should().Be("SyncedFromExternal");
        history.NewValue.Should().Be("Closed");
    }

    // =========================================================================
    // Tier 4: Real-World Application Scenarios (5 detailed multi-step scenarios)
    // =========================================================================

    [Fact]
    public async Task Scenario1_WorkItemReview_Approval_SyncAndBillingFlow_EndToEnd()
    {
        // --- Step 1: WorkItem Creation with Reviewer ---
        using var dbContext = TestDataBuilder.CreateInMemoryDbContext();
        var project = TestDataBuilder.CreateProject(enableSync: true, isSimulatedMode: false, pat: "secret-pat");
        var creator = TestDataBuilder.CreateEmployee(email: "john@corp.com", fullName: "John Engineer", hourlyCostRate: 50.00m, hourlyBillableRate: 150.00m);
        var reviewer = TestDataBuilder.CreateEmployee(email: "sarah@corp.com", fullName: "Sarah Lead", role: AppRole.TeamLead);

        dbContext.Projects.Add(project);
        dbContext.Employees.AddRange(creator, reviewer);
        await dbContext.SaveChangesAsync();

        var workItem = TestDataBuilder.CreateWorkItem(project.Id, creator.Id, "CRM-1001", "Implement Kafka Message Consumer");
        workItem.ReviewerId = reviewer.Id;
        workItem.Status = WorkItemStatus.PendingReview;
        workItem.ReviewStatus = ReviewStatus.Pending;
        dbContext.WorkItems.Add(workItem);
        await dbContext.SaveChangesAsync();

        var mockTracker = new Mock<IIssueTrackerProvider>();
        var mockEmail = new Mock<IEmailNotificationService>();
        var syncOrchestrator = new TestSyncOrchestratorService(dbContext, mockTracker.Object);
        var reviewService = new TestWorkItemReviewService(dbContext, mockEmail.Object, syncOrchestrator, reviewer.Id);

        // Review assignment triggers notification
        await mockEmail.Object.SendReviewAssignedEmailAsync(reviewer.Email, reviewer.FullName, workItem.Code, workItem.Title, workItem.Id);
        mockEmail.Verify(e => e.SendReviewAssignedEmailAsync(reviewer.Email, reviewer.FullName, workItem.Code, workItem.Title, workItem.Id, It.IsAny<CancellationToken>()), Times.Once);

        // Moving item on Kanban is blocked while in PendingReview
        workItem.CanTransitionKanban().Should().BeFalse();

        // --- Step 2: Clarification Cycle ---
        await reviewService.RequestClarificationAsync(workItem.Id, reviewer.Id, "Please specify partition retry strategy.");
        var itemAfterClarify = await dbContext.WorkItems.FindAsync(workItem.Id);
        itemAfterClarify!.ReviewStatus.Should().Be(ReviewStatus.ClarificationNeeded);
        itemAfterClarify.Status.Should().Be(WorkItemStatus.ClarificationRequested);

        mockEmail.Verify(e => e.SendReviewDecisionEmailAsync(
            creator.Email, creator.FullName, workItem.Code, workItem.Title, ReviewStatus.ClarificationNeeded, "Please specify partition retry strategy.", It.IsAny<CancellationToken>()), Times.Once);

        // Creator updates item and resubmits
        itemAfterClarify.Description += " Retry strategy: exponential backoff with dead-letter queue.";
        itemAfterClarify.Status = WorkItemStatus.PendingReview;
        itemAfterClarify.ReviewStatus = ReviewStatus.Pending;
        await dbContext.SaveChangesAsync();

        // --- Step 3: Approval and Outbound Sync ---
        mockTracker.Setup(t => t.PushWorkItemAsync(It.IsAny<ProjectExternalConfig>(), It.IsAny<WorkItem>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExternalWorkItemDto(
                "ADO-1001",
                "https://dev.azure.com/sample-org/SampleProject/_workitems/edit/1001",
                workItem.Title,
                workItem.Description,
                "To Do",
                "Task",
                null));

        await reviewService.ApproveReviewAsync(workItem.Id, reviewer.Id, "Approved for implementation.");
        var itemAfterApprove = await dbContext.WorkItems.FindAsync(workItem.Id);
        itemAfterApprove!.ReviewStatus.Should().Be(ReviewStatus.Approved);
        itemAfterApprove.Status.Should().Be(WorkItemStatus.Todo);
        itemAfterApprove.ExternalId.Should().Be("ADO-1001");

        // --- Step 4: Time Tracking on Approved Task ---
        itemAfterApprove.Status = WorkItemStatus.InProgress;
        await dbContext.SaveChangesAsync();

        var billableEntry = (DurationMinutes: 240m, HourlyBillableRate: creator.HourlyBillableRate, IsBillable: true); // 4.0h
        var nonBillableEntry = (DurationMinutes: 60m, HourlyBillableRate: creator.HourlyBillableRate, IsBillable: false); // 1.0h
        var allCostEntries = new List<(decimal DurationMinutes, decimal HourlyCostRate)>
        {
            (240m, creator.HourlyCostRate),
            (60m, creator.HourlyCostRate)
        };

        // --- Step 5: Financial Calculation Verification ---
        var revenue = FinancialCalculator.CalculateProjectRevenue(new[] { billableEntry, nonBillableEntry });
        var cost = FinancialCalculator.CalculateProjectInternalCost(allCostEntries);
        var (profit, margin) = FinancialCalculator.CalculateProfitability(revenue, cost);

        // Revenue: 4h * $150 = $600.00
        revenue.Should().Be(600.00m);
        // Cost: 5h * $50 = $250.00
        cost.Should().Be(250.00m);
        // Gross Profit: $600 - $250 = $350.00
        profit.Should().Be(350.00m);
        // Margin: (350 / 600) * 100 = 58.33%
        margin.Should().Be(58.33m);
    }

    [Fact]
    public async Task Scenario2_InboundAdoSync_LoopPreventionAndConflictResolution_EndToEnd()
    {
        // --- Step 1: Initial WorkItem Push to ADO ---
        using var dbContext = TestDataBuilder.CreateInMemoryDbContext();
        var project = TestDataBuilder.CreateProject(enableSync: true);
        var dev = TestDataBuilder.CreateEmployee(email: "maya@company.com", fullName: "Dev Maya");
        dbContext.Projects.Add(project);
        dbContext.Employees.Add(dev);

        var workItem = TestDataBuilder.CreateWorkItem(project.Id, dev.Id, "CRM-201", "Payment gateway integration", status: WorkItemStatus.Todo, reviewStatus: ReviewStatus.Approved);
        workItem.ExternalId = "ADO-201";
        var hash1 = SyncContentHasher.ComputeHash(workItem.Title, workItem.Description, workItem.Status, workItem.Type, dev.Email);
        workItem.LastSyncedContentHash = hash1;
        workItem.LastSyncOrigin = SyncOrigin.LocalCrm;
        dbContext.WorkItems.Add(workItem);
        await dbContext.SaveChangesAsync();

        var mockTracker = new Mock<IIssueTrackerProvider>();
        var syncEngine = new TestSyncOrchestratorService(dbContext, mockTracker.Object);

        // --- Step 2: Webhook Echo Suppression ---
        mockTracker.Setup(t => t.ParseWebhookPayloadAsync(It.IsAny<string>(), It.IsAny<IDictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExternalWorkItemDto(
                "ADO-201", "https://dev.azure.com/sample/201", workItem.Title, workItem.Description, "To Do", "Task", dev.Email));

        var echoResult = await syncEngine.ProcessInboundWebhookAsync(ExternalSourceType.AzureDevOps, "{}", new Dictionary<string, string>(), CancellationToken.None);
        echoResult.Status.Should().Be("IgnoredEcho");
        echoResult.IsEcho.Should().BeTrue();
        (await dbContext.WorkItemHistories.CountAsync()).Should().Be(0); // Zero history appended on echo

        // --- Step 3: Genuine External Edit Received ---
        mockTracker.Setup(t => t.ParseWebhookPayloadAsync(It.IsAny<string>(), It.IsAny<IDictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExternalWorkItemDto(
                "ADO-201", "https://dev.azure.com/sample/201", workItem.Title, workItem.Description, "Done", "Task", dev.Email));

        var updateResult = await syncEngine.ProcessInboundWebhookAsync(ExternalSourceType.AzureDevOps, "{\"update\":true}", new Dictionary<string, string>(), CancellationToken.None);
        updateResult.Status.Should().Be("Applied");
        updateResult.IsEcho.Should().BeFalse();

        var updatedItem = await dbContext.WorkItems.FindAsync(workItem.Id);
        updatedItem!.Status.Should().Be(WorkItemStatus.Done);
        updatedItem.LastSyncOrigin.Should().Be(SyncOrigin.ExternalWebhook);
        (await dbContext.WorkItemHistories.CountAsync()).Should().Be(1);

        // --- Step 4: Unlinked Remote Item Discovered & Conflict Registered ---
        var localUnlinkedItem = TestDataBuilder.CreateWorkItem(project.Id, dev.Id, "CRM-202", "Payment Gateway Integration V2");
        localUnlinkedItem.ExternalId = null;
        dbContext.WorkItems.Add(localUnlinkedItem);
        await dbContext.SaveChangesAsync();

        mockTracker.Setup(t => t.FetchRemoteWorkItemsAsync(It.IsAny<ProjectExternalConfig>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ExternalWorkItemDto>
            {
                new("ADO-305", "https://dev.azure.com/sample/305", "Payment gateway integration v2", "Version 2 of payment service", "New", "User Story", null)
            });

        var reconResult = await syncEngine.RunReconciliationAsync(project.Id, CancellationToken.None);
        reconResult.ConflictsCount.Should().Be(1);

        var conflict = await dbContext.SyncConflicts.FirstOrDefaultAsync(c => c.ExternalId == "ADO-305");
        conflict.Should().NotBeNull();
        conflict!.Status.Should().Be(ConflictStatus.Pending);
        conflict.ConflictType.Should().Be(SyncConflictType.DuplicateCandidate);
        conflict.SimilarityScore.Should().BeGreaterThanOrEqualTo(0.85);

        // --- Step 5: Conflict Resolution via Merge Action ---
        var resolutionResult = await syncEngine.ResolveConflictAsync(conflict.Id, ConflictResolutionAction.Merge, CancellationToken.None);
        resolutionResult.Resolved.Should().BeTrue();
        resolutionResult.Action.Should().Be(ConflictResolutionAction.Merge);

        var reloadedLocal = await dbContext.WorkItems.FindAsync(localUnlinkedItem.Id);
        reloadedLocal!.ExternalId.Should().Be("ADO-305");
    }

    [Fact]
    public async Task Scenario3_DailyPlanningCutoff_ReminderEmailAndIdempotentAudit_EndToEnd()
    {
        // --- Step 1: Cutoff Configuration ---
        using var dbContext = TestDataBuilder.CreateInMemoryDbContext();
        var timeProvider = new TestTimeProvider(DateTimeOffset.Parse("2026-11-16T10:15:00Z")); // 10:15 AM local

        var config = TestDataBuilder.CreateCutoffConfig(new TimeOnly(10, 0, 0), "UTC");
        dbContext.PlanningCutoffConfigs.Add(config);

        var alice = TestDataBuilder.CreateEmployee(email: "alice@company.com", fullName: "Alice Comply");
        var bob = TestDataBuilder.CreateEmployee(email: "bob@company.com", fullName: "Bob Slacker");
        var charlie = TestDataBuilder.CreateEmployee(email: "charlie@company.com", fullName: "Charlie Busy");
        dbContext.Employees.AddRange(alice, bob, charlie);

        // --- Step 2: On-Time Plan Submission (Alice at 09:30 AM) ---
        var alicePlan = new DailyPlan
        {
            Id = Guid.NewGuid(),
            EmployeeId = alice.Id,
            PlanDate = new DateOnly(2026, 11, 16),
            SubmittedAt = DateTimeOffset.Parse("2026-11-16T09:30:00Z")
        };
        alicePlan.EvaluateSubmissionTiming(config);
        dbContext.DailyPlans.Add(alicePlan);
        await dbContext.SaveChangesAsync();

        alicePlan.IsSubmittedOnTime.Should().BeTrue();

        var mockEmail = new Mock<IEmailNotificationService>();
        var engine = new TestPlanningCutoffJob(dbContext, mockEmail.Object, timeProvider);

        // --- Step 3: First Cutoff Check Execution (at 10:15 AM) ---
        var summary1 = await engine.ExecuteCutoffCheckAsync(CancellationToken.None);

        // Assert: Alice skipped, Bob & Charlie notified
        summary1.RemindersSent.Should().Be(2);
        summary1.NotifiedEmployeeEmails.Should().Contain(new[] { "bob@company.com", "charlie@company.com" });
        (await dbContext.DailyPlanReminderLogs.CountAsync()).Should().Be(2);

        mockEmail.Verify(e => e.SendCutoffReminderEmailAsync("bob@company.com", "Bob Slacker", new DateOnly(2026, 11, 16), config.CutoffTimeLocal, It.IsAny<CancellationToken>()), Times.Once);
        mockEmail.Verify(e => e.SendCutoffReminderEmailAsync("charlie@company.com", "Charlie Busy", new DateOnly(2026, 11, 16), config.CutoffTimeLocal, It.IsAny<CancellationToken>()), Times.Once);

        // --- Step 4: Repeated Job Execution (Idempotency Check at 10:30 AM) ---
        timeProvider.Advance(TimeSpan.FromMinutes(15));
        var summary2 = await engine.ExecuteCutoffCheckAsync(CancellationToken.None);

        // Assert: 0 additional emails sent; idempotency strictly preserved!
        summary2.RemindersSent.Should().Be(0);
        (await dbContext.DailyPlanReminderLogs.CountAsync()).Should().Be(2);
        mockEmail.Verify(e => e.SendCutoffReminderEmailAsync("bob@company.com", It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<TimeOnly>(), It.IsAny<CancellationToken>()), Times.Once);

        // --- Step 5: Late Plan Submission (Bob submits at 10:45 AM) ---
        var bobLatePlan = new DailyPlan
        {
            Id = Guid.NewGuid(),
            EmployeeId = bob.Id,
            PlanDate = new DateOnly(2026, 11, 16),
            SubmittedAt = DateTimeOffset.Parse("2026-11-16T10:45:00Z")
        };
        bobLatePlan.EvaluateSubmissionTiming(config);
        dbContext.DailyPlans.Add(bobLatePlan);
        await dbContext.SaveChangesAsync();

        bobLatePlan.IsSubmittedOnTime.Should().BeFalse();
        var allPlans = await dbContext.DailyPlans.ToListAsync();
        allPlans.Count(p => p.IsSubmittedOnTime).Should().Be(1); // Only Alice was on time
    }

    [Fact]
    public void Scenario4_RbacMultiPersonaSecurityAndAccessEnforcement_EndToEnd()
    {
        var rbacService = new RbacAuthorizationService();

        // --- Step 1: Unauthenticated Caller ---
        var unauthenticatedCaller = new SecurityContext(false, null);
        var canAccessManagement = rbacService.CanAccessManagementPortal(unauthenticatedCaller);
        var authStatus = rbacService.GetAuthorizationStatusCode(unauthenticatedCaller, "/api/management/compensation");
        canAccessManagement.Should().BeFalse();
        authStatus.Should().Be(401); // 401 Unauthorized

        // --- Step 2: Employee Persona Restrictions ---
        var employeeCaller = new SecurityContext(true, AppRole.Employee);
        rbacService.CanAccessPublicDirectory(employeeCaller).Should().BeTrue();
        rbacService.CanAccessManagementPortal(employeeCaller).Should().BeFalse();
        rbacService.GetAuthorizationStatusCode(employeeCaller, "/api/management/compensation").Should().Be(403);
        rbacService.GetAuthorizationStatusCode(employeeCaller, "/api/management/profitability").Should().Be(403);
        rbacService.GetAuthorizationStatusCode(employeeCaller, "/api/planning/cutoff-config").Should().Be(403);

        // --- Step 3: TeamLead Persona Capabilities & Boundaries ---
        var teamLeadCaller = new SecurityContext(true, AppRole.TeamLead);
        rbacService.CanAccessReviewQueue(teamLeadCaller).Should().BeTrue();
        rbacService.CanManageCutoffConfig(teamLeadCaller).Should().BeTrue();
        rbacService.CanAccessManagementPortal(teamLeadCaller).Should().BeFalse();
        rbacService.GetAuthorizationStatusCode(teamLeadCaller, "/api/management/compensation").Should().Be(403);
        rbacService.GetAuthorizationStatusCode(teamLeadCaller, "/api/management/profitability").Should().Be(403);

        // --- Step 4: HigherManagement Full Authorization ---
        var managementCaller = new SecurityContext(true, AppRole.HigherManagement);
        rbacService.CanAccessManagementPortal(managementCaller).Should().BeTrue();
        rbacService.GetAuthorizationStatusCode(managementCaller, "/api/management/compensation").Should().Be(200);
        rbacService.GetAuthorizationStatusCode(managementCaller, "/api/management/profitability").Should().Be(200);
        rbacService.GetAuthorizationStatusCode(managementCaller, "/api/management/analytics/velocity").Should().Be(200);
        rbacService.CanManageCutoffConfig(managementCaller).Should().BeTrue();
    }

    [Fact]
    public void Scenario5_TimerConcurrency_BillableLoggingAndUtilizationAggregation_EndToEnd()
    {
        // --- Step 1: First Live Timer Started on Task 1 ---
        var dev = TestDataBuilder.CreateEmployee(fullName: "Dev Charlie", hourlyCostRate: 40.00m, hourlyBillableRate: 120.00m, weeklyCapacityHours: 40.0m);
        var task1Id = Guid.NewGuid();
        var task2Id = Guid.NewGuid();

        var timerManager = new TimerConcurrencyManager();
        var timer1 = timerManager.StartTimer(dev.Id, task1Id, isBillable: true);
        timer1.IsRunning.Should().BeTrue();
        timer1.EndedAt.Should().BeNull();

        // --- Step 2: Concurrent Timer Start on Task 2 (Auto-Stop Task 1) ---
        timer1.StartedAt = DateTimeOffset.UtcNow.AddMinutes(-60); // 1 hour elapsed

        var timer2 = timerManager.StartTimer(dev.Id, task2Id, isBillable: true);
        // Timer 1 must be auto-finalized
        timer1.IsRunning.Should().BeFalse();
        timer1.EndedAt.Should().NotBeNull();
        timer1.DurationMinutes.Should().Be(60);

        // Timer 2 is active
        timer2.IsRunning.Should().BeTrue();

        // --- Step 3: Finalize Timer 2 & Add Manual Non-Billable Entry ---
        timer2.StartedAt = DateTimeOffset.UtcNow.AddMinutes(-180); // 3 hours elapsed
        timerManager.StopTimer(timer2);
        timer2.DurationMinutes.Should().Be(180);

        // Manual entry: 2.0h non-billable (120 minutes)
        var manualEntry = new TimeEntry
        {
            Id = Guid.NewGuid(),
            EmployeeId = dev.Id,
            DurationMinutes = 120,
            IsBillable = false
        };

        var allEntries = new List<TimeEntry> { timer1, timer2, manualEntry };

        // --- Step 4: Utilization and Financial Aggregation ---
        // Total Hours = 1h + 3h + 2h = 6.0h
        // Billable Hours = 1h + 3h = 4.0h
        var billableHours = allEntries.Where(e => e.IsBillable).Sum(e => e.DurationMinutes / 60.0m);
        billableHours.Should().Be(4.0m);

        var utilization = FinancialCalculator.CalculateUtilizationPercent(billableHours, dev.WeeklyCapacityHours);
        // Utilization: (4.0 / 40.0) * 100 = 10.00%
        utilization.Should().Be(10.00m);

        var revenueEntries = allEntries.Select(e => ((decimal)e.DurationMinutes, dev.HourlyBillableRate, e.IsBillable));
        var costEntries = allEntries.Select(e => ((decimal)e.DurationMinutes, dev.HourlyCostRate));

        var revenue = FinancialCalculator.CalculateProjectRevenue(revenueEntries);
        var cost = FinancialCalculator.CalculateProjectInternalCost(costEntries);
        var (profit, margin) = FinancialCalculator.CalculateProfitability(revenue, cost);

        // Revenue: 4h * $120 = $480.00
        revenue.Should().Be(480.00m);
        // Cost: 6h * $40 = $240.00
        cost.Should().Be(240.00m);
        // Profit: $480 - $240 = $240.00
        profit.Should().Be(240.00m);
        // Margin: (240 / 480) * 100 = 50.00%
        margin.Should().Be(50.00m);
    }
}
