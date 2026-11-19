using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CompanyCRM.Application.DTOs;
using CompanyCRM.Application.Interfaces;
using CompanyCRM.Application.Services;
using CompanyCRM.Domain.Entities;
using CompanyCRM.Domain.Enums;
using CompanyCRM.Domain.ValueObjects;
using CompanyCRM.Infrastructure.ExternalSync;
using CompanyCRM.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace CompanyCRM.Tests.Infrastructure;

/// <summary>
/// EMPIRICAL CHALLENGER STRESS HARNESS — Milestone 2: Infrastructure, External Sync & Background Jobs.
/// Adversarially tests:
/// 1. Infinite Webhook Echo Loop Prevention (rapid burst, 0 mutations, 0 history, no outbound push)
/// 2. Canonical Content Hashing Stress (CRLF vs LF, whitespace, email casing, nulls, unicode, pipe delimiter collision)
/// 3. Deduplication Similarity Metric & Conflict Lifecycle (0.84 vs 0.85 boundary, punctuation, repeat resolution, Skip idempotency)
/// 4. Outbound Push Gating (Draft, PendingReview, ClarificationRequested, Denied)
/// </summary>
public class ChallengerMilestone2StressTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<CrmDbContext> _options;
    private readonly AzureDevOpsSimulator _simulator;
    private readonly AzureDevOpsIssueTrackerProvider _realProvider;

    public ChallengerMilestone2StressTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<CrmDbContext>()
            .UseSqlite(_connection)
            .Options;

        _simulator = new AzureDevOpsSimulator();
        _realProvider = new AzureDevOpsIssueTrackerProvider(
            new System.Net.Http.HttpClient(),
            _simulator,
            NullLogger<AzureDevOpsIssueTrackerProvider>.Instance);
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    private CrmDbContext CreateContext() => new CrmDbContext(_options);

    private SyncOrchestratorService CreateService(CrmDbContext context, IIssueTrackerProvider? provider = null)
    {
        return new SyncOrchestratorService(
            context,
            provider ?? _realProvider,
            NullLogger<SyncOrchestratorService>.Instance);
    }

    private async Task<(Project project, Employee employee, WorkItem workItem)> SeedBasicWorkItemAsync(
        CrmDbContext context,
        WorkItemStatus status = WorkItemStatus.Todo,
        ReviewStatus reviewStatus = ReviewStatus.Approved,
        string title = "Implement Two-Way Sync",
        string description = "Full bidirectional synchronization with Azure DevOps")
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
            Title = title,
            Description = description,
            Status = status,
            Type = WorkItemType.Task,
            ReviewStatus = reviewStatus
        };

        context.WorkItems.Add(workItem);
        await context.SaveChangesAsync();

        return (project, employee, workItem);
    }

    // =========================================================================
    // FOCUS 1: Infinite Webhook Echo Loop Prevention
    // =========================================================================

    [Fact]
    public async Task EchoLoopPrevention_RapidBurstOfMatchingWebhooks_ReturnsIgnoredEcho_WithZeroMutationsAndZeroPushes()
    {
        using var context = CreateContext();
        var (project, employee, workItem) = await SeedBasicWorkItemAsync(context);

        var mockProvider = new Mock<IIssueTrackerProvider>();
        mockProvider.Setup(p => p.SourceType).Returns(ExternalSourceType.AzureDevOps);

        var service = CreateService(context, mockProvider.Object);

        // Pre-condition: Item was already pushed to external system
        var canonicalHash = SyncContentHasher.ComputeHash(
            workItem.Title,
            workItem.Description,
            workItem.Status,
            workItem.Type,
            employee.Email);

        workItem.ExternalSource = ExternalSourceType.AzureDevOps;
        workItem.ExternalId = "ADO-9999";
        workItem.ExternalUrl = "https://dev.azure.com/test-org/CRMProject/_workitems/edit/ADO-9999";
        workItem.LastSyncedContentHash = canonicalHash;
        workItem.LastSyncOrigin = SyncOrigin.LocalCrm;
        workItem.LastSyncedAt = DateTimeOffset.UtcNow;

        var initialHistory = new WorkItemHistory
        {
            Id = Guid.NewGuid(),
            WorkItemId = workItem.Id,
            ChangedById = employee.Id,
            Action = "SyncedToExternal",
            FieldName = "ExternalId",
            NewValue = "ADO-9999",
            ChangedAt = DateTimeOffset.UtcNow
        };
        context.WorkItemHistories.Add(initialHistory);
        await context.SaveChangesAsync();

        var baselineHistoryCount = await context.WorkItemHistories.CountAsync();
        var baselineItemUpdatedAt = workItem.UpdatedAt;

        // Mock inbound webhook parsing to return matching content
        mockProvider.Setup(p => p.ParseWebhookPayloadAsync(It.IsAny<string>(), It.IsAny<IDictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExternalWorkItemDto(
                ExternalId: "ADO-9999",
                ExternalUrl: "https://dev.azure.com/test-org/CRMProject/_workitems/edit/ADO-9999",
                Title: workItem.Title,
                Description: workItem.Description,
                Status: "To Do", // Maps to Todo
                Type: "Task",     // Maps to Task
                AssigneeEmail: employee.Email));

        // Act: Fire rapid burst of 50 repeated webhook calls mimicking a tight ping-pong echo loop
        const int burstCount = 50;
        var headers = new Dictionary<string, string> { { "X-VSS-SubscriptionId", "test-sub" } };
        var rawPayload = "{\"eventType\":\"workitem.updated\",\"resource\":{\"id\":9999}}";

        var results = new List<WebhookProcessingResultDto>();
        for (int i = 0; i < burstCount; i++)
        {
            var result = await service.ProcessInboundWebhookAsync(
                ExternalSourceType.AzureDevOps,
                rawPayload,
                headers);
            results.Add(result);
        }

        // Assert 1: Every single webhook in the burst was strictly recognized as an echo
        results.Should().HaveCount(burstCount);
        results.Should().OnlyContain(r => r.Status == "IgnoredEcho" && r.IsEcho == true);

        // Assert 2: ZERO database mutations - WorkItem fields and timestamps remain unchanged
        using var verifyContext = CreateContext();
        var itemAfterBurst = await verifyContext.WorkItems.FindAsync(workItem.Id);
        itemAfterBurst!.LastSyncedContentHash.Should().Be(canonicalHash);
        itemAfterBurst.LastSyncOrigin.Should().Be(SyncOrigin.LocalCrm);
        itemAfterBurst.UpdatedAt.Should().Be(baselineItemUpdatedAt);

        // Assert 3: ZERO duplicate WorkItemHistory records appended
        var currentHistoryCount = await verifyContext.WorkItemHistories.CountAsync();
        currentHistoryCount.Should().Be(baselineHistoryCount, "Ignored echo webhooks MUST NOT append audit history records");

        // Assert 4: NEVER triggers an outbound push (prevents infinite ping-pong)
        mockProvider.Verify(p => p.PushWorkItemAsync(
            It.IsAny<ProjectExternalConfig>(),
            It.IsAny<WorkItem>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EchoLoopPrevention_GenuineRemoteModification_AppliesUpdate_AndSecondIdenticalWebhookIsIgnored()
    {
        using var context = CreateContext();
        var (project, employee, workItem) = await SeedBasicWorkItemAsync(context);

        var mockProvider = new Mock<IIssueTrackerProvider>();
        mockProvider.Setup(p => p.SourceType).Returns(ExternalSourceType.AzureDevOps);

        var service = CreateService(context, mockProvider.Object);

        // Link item to ADO
        var originalHash = SyncContentHasher.ComputeHash(workItem.Title, workItem.Description, workItem.Status, workItem.Type, employee.Email);
        workItem.ExternalSource = ExternalSourceType.AzureDevOps;
        workItem.ExternalId = "ADO-1234";
        workItem.LastSyncedContentHash = originalHash;
        workItem.LastSyncOrigin = SyncOrigin.LocalCrm;
        await context.SaveChangesAsync();

        // 1. Inbound webhook arrives with remote status changed from Active (Todo) to Resolved (InVerify)
        mockProvider.Setup(p => p.ParseWebhookPayloadAsync(It.IsAny<string>(), It.IsAny<IDictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExternalWorkItemDto(
                ExternalId: "ADO-1234",
                ExternalUrl: "https://dev.azure.com/test-org/CRMProject/_workitems/edit/ADO-1234",
                Title: workItem.Title,
                Description: workItem.Description,
                Status: "Resolved", // Maps to InVerify
                Type: "Task",
                AssigneeEmail: employee.Email));

        var headers = new Dictionary<string, string>();
        var result1 = await service.ProcessInboundWebhookAsync(ExternalSourceType.AzureDevOps, "{}", headers);

        result1.Status.Should().Be("Applied");
        result1.IsEcho.Should().BeFalse();

        // Verify local state updated to InVerify and ExternalWebhook origin
        using var verifyContext1 = CreateContext();
        var itemUpdated = await verifyContext1.WorkItems.FindAsync(workItem.Id);
        itemUpdated!.Status.Should().Be(WorkItemStatus.InVerify);
        itemUpdated.LastSyncOrigin.Should().Be(SyncOrigin.ExternalWebhook);

        // 2. Azure DevOps echoes the same webhook again (tight loop echo)
        var result2 = await service.ProcessInboundWebhookAsync(ExternalSourceType.AzureDevOps, "{}", headers);

        result2.Status.Should().Be("IgnoredEcho");
        result2.IsEcho.Should().BeTrue();

        // Zero additional history records on second echo
        var totalHistories = await verifyContext1.WorkItemHistories.CountAsync(h => h.WorkItemId == workItem.Id);
        totalHistories.Should().Be(1); // Only the 1 genuine remote update logged
    }

    // =========================================================================
    // FOCUS 2: Canonical Content Hashing Stress
    // =========================================================================

    [Fact]
    public void CanonicalHashing_CrlfVsLfInDescription_ProducesIdenticalHash()
    {
        var crlfDesc = "Step 1: Open app\r\nStep 2: Click login\r\nStep 3: Observe crash\r\n";
        var lfDesc = "Step 1: Open app\nStep 2: Click login\nStep 3: Observe crash\n";

        var hashCrlf = SyncContentHasher.ComputeHash("Login Crash", crlfDesc, WorkItemStatus.Todo, WorkItemType.Bug, "dev@co.com");
        var hashLf = SyncContentHasher.ComputeHash("Login Crash", lfDesc, WorkItemStatus.Todo, WorkItemType.Bug, "dev@co.com");

        hashCrlf.Should().Be(hashLf, "CRLF and LF line endings in Description must be normalized to prevent false echo loops");
    }

    [Fact]
    public void CanonicalHashing_TrailingWhitespace_ProducesIdenticalHash()
    {
        var titleNormal = "Database Migration";
        var titleWithSpaces = "   Database Migration   ";

        var descNormal = "Execute EF Core migration";
        var descWithSpaces = "Execute EF Core migration   \r\n  ";

        var hash1 = SyncContentHasher.ComputeHash(titleNormal, descNormal, WorkItemStatus.Todo, WorkItemType.Task, "dev@co.com");
        var hash2 = SyncContentHasher.ComputeHash(titleWithSpaces, descWithSpaces, WorkItemStatus.Todo, WorkItemType.Task, "dev@co.com");

        hash1.Should().Be(hash2, "Leading and trailing whitespace must be normalized");
    }

    [Fact]
    public void CanonicalHashing_AssigneeEmailCasingVariations_ProducesIdenticalHash()
    {
        var email1 = "John.Doe@CompanyCorp.COM";
        var email2 = "john.doe@companycorp.com";
        var email3 = "  JOHN.DOE@companycorp.com  ";

        var hash1 = SyncContentHasher.ComputeHash("Title", "Desc", WorkItemStatus.Todo, WorkItemType.Task, email1);
        var hash2 = SyncContentHasher.ComputeHash("Title", "Desc", WorkItemStatus.Todo, WorkItemType.Task, email2);
        var hash3 = SyncContentHasher.ComputeHash("Title", "Desc", WorkItemStatus.Todo, WorkItemType.Task, email3);

        hash1.Should().Be(hash2);
        hash2.Should().Be(hash3);
    }

    [Fact]
    public void CanonicalHashing_NullVsEmptyVsWhitespaceDescription_ProducesIdenticalHash()
    {
        var hashNull = SyncContentHasher.ComputeHash("Task Title", null, WorkItemStatus.Backlog, WorkItemType.Story, null);
        var hashEmpty = SyncContentHasher.ComputeHash("Task Title", "", WorkItemStatus.Backlog, WorkItemType.Story, "");
        var hashWhitespace = SyncContentHasher.ComputeHash("Task Title", "   \r\n  ", WorkItemStatus.Backlog, WorkItemType.Story, "   ");

        hashNull.Should().Be(hashEmpty);
        hashEmpty.Should().Be(hashWhitespace);
    }

    [Fact]
    public void CanonicalHashing_UnicodeMultilingualStrings_ProducesDeterministicSha256()
    {
        var title = "🐛 Баг входа: ユーザー認証 [Überweisung €500]";
        var desc = "RTL Arabic: خطأ في تسجيل الدخول\nJapanese: ログイン画面が動作しません\nEmoji: 🚀🔥";

        var hash1 = SyncContentHasher.ComputeHash(title, desc, WorkItemStatus.InProgress, WorkItemType.Bug, "engineer@domain.com");
        var hash2 = SyncContentHasher.ComputeHash(title, desc, WorkItemStatus.InProgress, WorkItemType.Bug, "engineer@domain.com");

        hash1.Should().HaveLength(64);
        hash1.Should().Be(hash2);
    }

    [Fact]
    public void CanonicalHashing_AdversarialChallenge_PipeDelimiterCollision_ExposesDelimiterVulnerability()
    {
        // ADVERSARIAL STRESS TEST:
        // Item A: Title contains pipe delimiter "Feature A|Subpart", Description is "Details"
        // Item B: Title is "Feature A", Description is "Subpart|Details"
        // Because canonical string is $"{title}|{desc}|{status}|{type}|{email}", both result in "Feature A|Subpart|Details|Todo|Task|dev@co.com"
        var hashA = SyncContentHasher.ComputeHash("Feature A|Subpart", "Details", WorkItemStatus.Todo, WorkItemType.Task, "dev@co.com");
        var hashB = SyncContentHasher.ComputeHash("Feature A", "Subpart|Details", WorkItemStatus.Todo, WorkItemType.Task, "dev@co.com");

        // Delimiter escaping in title and description prevents collision between distinct fields:
        var producesCollision = (hashA == hashB);
        producesCollision.Should().BeFalse("Escaped pipe delimiters prevent canonical hash collisions between distinct fields");
    }

    // =========================================================================
    // FOCUS 3: Deduplication Similarity Metric & Conflict Lifecycle
    // =========================================================================

    [Theory]
    [InlineData("Payment Gateway Integration", "Payment Gateway Integration", 1.0)]      // Exact
    [InlineData("Invoice PDF Generator Service", "Generator Service Invoice PDF", 1.0)]    // Reordered Jaccard = 1.0
    public void TitleSimilarity_HighSimilarity_ScoresAboveThreshold(string a, string b, double minScore)
    {
        var sim = SyncOrchestratorService.ComputeTitleSimilarity(a, b);
        sim.Should().BeGreaterOrEqualTo(minScore);
        sim.Should().BeGreaterOrEqualTo(0.85);
    }

    [Fact]
    public void TitleSimilarity_BoundaryThresholdStress_DistinguishesAboveAndBelow085()
    {
        // 1. Boundary case: Small single-word edit on a medium-length sentence
        // "Implement OAuth2 Authentication Flow in Core Service" (53 chars)
        // vs "Implement OAuth2 Authorization Flow in Core Service" (54 chars)
        // Levenshtein: distance 5 ("Authentication" vs "Authorization", length 14 vs 13)
        // distance=5, maxLen=54 -> Levenshtein = 1 - 5/54 = 0.907 >= 0.85
        var simNearMatch = SyncOrchestratorService.ComputeTitleSimilarity(
            "Implement OAuth2 Authentication Flow in Core Service",
            "Implement OAuth2 Authorization Flow in Core Service");
        simNearMatch.Should().BeGreaterOrEqualTo(0.85);

        // 2. Clear difference: Different features entirely
        var simDivergent = SyncOrchestratorService.ComputeTitleSimilarity(
            "Implement OAuth2 Authentication Flow",
            "Refactor Database Connection Pool for SQLite");
        simDivergent.Should().BeLessThan(0.40);
    }

    [Fact]
    public void TitleSimilarity_PunctuationBracketTokens_ProducesExactMatch()
    {
        // With '[' and ']' included in Tokenize() delimiters:
        // "[BUG] Fix Login" and "BUG: Fix Login" both tokenize to {"bug", "fix", "login"}.
        var simWithBrackets = SyncOrchestratorService.ComputeTitleSimilarity(
            "[BUG] Fix Login",
            "BUG: Fix Login");

        simWithBrackets.Should().Be(1.0, "Bracket-stripped tokens produce identical token sets (Jaccard = 1.0)");
    }

    [Fact]
    public async Task DeduplicationLifecycle_ResolveWithMerge_LinksExternalId_AndBlocksRepeatResolution()
    {
        using var context = CreateContext();
        var (project, employee, localItem) = await SeedBasicWorkItemAsync(context);
        var service = CreateService(context);

        var conflict = new SyncConflict
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            LocalWorkItemId = localItem.Id,
            ExternalSource = ExternalSourceType.AzureDevOps,
            ExternalId = "ADO-5001",
            ExternalTitle = localItem.Title,
            ExternalDescription = localItem.Description,
            ExternalStatus = "Active",
            SimilarityScore = 0.95,
            ConflictType = SyncConflictType.DuplicateCandidate,
            Status = ConflictStatus.Pending,
            ResolutionAction = ConflictResolutionAction.Unresolved,
            DetectedAt = DateTimeOffset.UtcNow
        };
        context.SyncConflicts.Add(conflict);
        await context.SaveChangesAsync();

        // 1. Resolve with Merge
        var result = await service.ResolveConflictAsync(conflict.Id, ConflictResolutionAction.Merge);
        result.Resolved.Should().BeTrue();

        using var verifyContext = CreateContext();
        var mergedItem = await verifyContext.WorkItems.FindAsync(localItem.Id);
        mergedItem!.ExternalId.Should().Be("ADO-5001");
        mergedItem.LastSyncOrigin.Should().Be(SyncOrigin.LocalCrm);

        var resolvedConflict = await verifyContext.SyncConflicts.FindAsync(conflict.Id);
        resolvedConflict!.Status.Should().Be(ConflictStatus.Resolved);
        resolvedConflict.ResolutionAction.Should().Be(ConflictResolutionAction.Merge);

        // 2. Repeat invocation on already resolved conflict MUST throw InvalidOperationException
        var repeatAct = async () => await service.ResolveConflictAsync(conflict.Id, ConflictResolutionAction.Merge);
        await repeatAct.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already resolved*");
    }

    [Fact]
    public async Task DeduplicationLifecycle_ResolveWithKeepBoth_CreatesPrefixedWorkItem_AndBlocksRepeat()
    {
        using var context = CreateContext();
        var (project, employee, localItem) = await SeedBasicWorkItemAsync(context);
        var service = CreateService(context);

        var conflict = new SyncConflict
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            LocalWorkItemId = localItem.Id,
            ExternalSource = ExternalSourceType.AzureDevOps,
            ExternalId = "5002",
            ExternalTitle = "Imported Separate Task",
            ExternalDescription = "Separate remote description",
            ExternalStatus = "Active",
            SimilarityScore = 0.88,
            ConflictType = SyncConflictType.DuplicateCandidate,
            Status = ConflictStatus.Pending,
            ResolutionAction = ConflictResolutionAction.Unresolved,
            DetectedAt = DateTimeOffset.UtcNow
        };
        context.SyncConflicts.Add(conflict);
        await context.SaveChangesAsync();

        // 1. Resolve with KeepBoth
        var result = await service.ResolveConflictAsync(conflict.Id, ConflictResolutionAction.KeepBoth);
        result.Resolved.Should().BeTrue();

        using var verifyContext = CreateContext();
        var newItem = await verifyContext.WorkItems.FirstOrDefaultAsync(w => w.Code == "ADO-5002");
        newItem.Should().NotBeNull();
        newItem!.Title.Should().Be("Imported Separate Task");
        newItem.ExternalId.Should().Be("5002");
        newItem.ReviewStatus.Should().Be(ReviewStatus.NotRequired);

        // 2. Repeat invocation MUST throw InvalidOperationException
        var repeatAct = async () => await service.ResolveConflictAsync(conflict.Id, ConflictResolutionAction.KeepBoth);
        await repeatAct.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already resolved*");
    }

    [Fact]
    public async Task DeduplicationLifecycle_ResolveWithSkip_PermanentlySuppressesFutureConflictsAndImports()
    {
        using var context = CreateContext();
        var (project, employee, localItem) = await SeedBasicWorkItemAsync(context);

        var mockProvider = new Mock<IIssueTrackerProvider>();
        mockProvider.Setup(p => p.SourceType).Returns(ExternalSourceType.AzureDevOps);
        var service = CreateService(context, mockProvider.Object);

        var conflict = new SyncConflict
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            LocalWorkItemId = localItem.Id,
            ExternalSource = ExternalSourceType.AzureDevOps,
            ExternalId = "ADO-5003",
            ExternalTitle = localItem.Title,
            ExternalDescription = localItem.Description,
            ExternalStatus = "Active",
            SimilarityScore = 0.92,
            ConflictType = SyncConflictType.DuplicateCandidate,
            Status = ConflictStatus.Pending,
            ResolutionAction = ConflictResolutionAction.Unresolved,
            DetectedAt = DateTimeOffset.UtcNow
        };
        context.SyncConflicts.Add(conflict);
        await context.SaveChangesAsync();

        // 1. Resolve with Skip
        var result = await service.ResolveConflictAsync(conflict.Id, ConflictResolutionAction.Skip);
        result.Resolved.Should().BeTrue();

        // 2. Verify subsequent inbound webhook for that external ID returns "Skipped" and does not create conflicts
        mockProvider.Setup(p => p.ParseWebhookPayloadAsync(It.IsAny<string>(), It.IsAny<IDictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExternalWorkItemDto(
                ExternalId: "ADO-5003",
                ExternalUrl: "https://dev.azure.com/test-org/CRMProject/_workitems/edit/ADO-5003",
                Title: localItem.Title,
                Description: localItem.Description,
                Status: "Active",
                Type: "Task",
                AssigneeEmail: employee.Email));

        var webhookResult = await service.ProcessInboundWebhookAsync(
            ExternalSourceType.AzureDevOps,
            "{}",
            new Dictionary<string, string>());

        webhookResult.Status.Should().Be("Skipped");
        webhookResult.Message.Should().Contain("previously marked as Skip");

        // 3. Verify subsequent background reconciliation also skips this external ID without recreating conflicts
        mockProvider.Setup(p => p.FetchRemoteWorkItemsAsync(It.IsAny<ProjectExternalConfig>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ExternalWorkItemDto>
            {
                new(
                    ExternalId: "ADO-5003",
                    ExternalUrl: "https://dev.azure.com/test-org/CRMProject/_workitems/edit/ADO-5003",
                    Title: localItem.Title,
                    Description: localItem.Description,
                    Status: "Active",
                    Type: "Task",
                    AssigneeEmail: employee.Email)
            });

        var reconResult = await service.RunReconciliationAsync(project.Id);
        reconResult.SkippedCount.Should().Be(1);
        reconResult.ConflictsCount.Should().Be(0);

        // Verify only 1 conflict exists in DB (the original resolved Skip)
        using var verifyContext = CreateContext();
        var pendingConflicts = await verifyContext.SyncConflicts
            .Where(c => c.ExternalId == "ADO-5003" && c.Status == ConflictStatus.Pending)
            .ToListAsync();
        pendingConflicts.Should().BeEmpty("Once marked Skip, no pending conflicts should ever be re-created");
    }

    // =========================================================================
    // FOCUS 4: Outbound Push Gating
    // =========================================================================

    [Theory]
    [InlineData(WorkItemStatus.PendingReview, ReviewStatus.Pending)]
    [InlineData(WorkItemStatus.Denied, ReviewStatus.Denied)]
    [InlineData(WorkItemStatus.ClarificationRequested, ReviewStatus.ClarificationNeeded)]
    public async Task OutboundPushGating_WhenReviewBlocks_ThrowsInvalidOperationException_AndNeverContactsProvider(
        WorkItemStatus status, ReviewStatus reviewStatus)
    {
        using var context = CreateContext();
        var (project, employee, workItem) = await SeedBasicWorkItemAsync(context, status, reviewStatus);

        var mockProvider = new Mock<IIssueTrackerProvider>();
        mockProvider.Setup(p => p.SourceType).Returns(ExternalSourceType.AzureDevOps);
        var service = CreateService(context, mockProvider.Object);

        // Act
        var act = async () => await service.PushWorkItemToExternalAsync(workItem.Id);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Cannot push work item*");

        // Verify external provider was NEVER contacted
        mockProvider.Verify(p => p.PushWorkItemAsync(
            It.IsAny<ProjectExternalConfig>(),
            It.IsAny<WorkItem>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task OutboundPushGating_AdversarialChallenge_DraftStatus_ShouldBeRejected()
    {
        // ADVERSARIAL STRESS TEST:
        // A newly created work item in Draft status (ReviewStatus.NotRequired)
        // Requirement 4 states:
        // "Verify that calling outbound push on items in `Draft`, `PendingReview`, `ClarificationRequested`, or `Denied` is rejected with `InvalidOperationException` and never contacts the external provider."
        using var context = CreateContext();
        var (project, employee, workItem) = await SeedBasicWorkItemAsync(
            context,
            status: WorkItemStatus.Draft,
            reviewStatus: ReviewStatus.NotRequired);

        var mockProvider = new Mock<IIssueTrackerProvider>();
        mockProvider.Setup(p => p.SourceType).Returns(ExternalSourceType.AzureDevOps);
        var service = CreateService(context, mockProvider.Object);

        // Testing whether Draft is rejected as required by Requirement 4
        var canPush = workItem.CanPushToExternal();
        canPush.Should().BeFalse("Draft status must block outbound push even if ReviewStatus is NotRequired");

        var act = async () => await service.PushWorkItemToExternalAsync(workItem.Id);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Cannot push work item*");

        mockProvider.Verify(p => p.PushWorkItemAsync(
            It.IsAny<ProjectExternalConfig>(),
            It.IsAny<WorkItem>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task OutboundPushGating_AdversarialChallenge_ClarificationRequestedWithNotRequiredReview_ShouldBeRejected()
    {
        // ADVERSARIAL STRESS TEST:
        // An item where Status is ClarificationRequested, but ReviewStatus is NotRequired or Approved.
        using var context = CreateContext();
        var (project, employee, workItem) = await SeedBasicWorkItemAsync(
            context,
            status: WorkItemStatus.ClarificationRequested,
            reviewStatus: ReviewStatus.NotRequired);

        var mockProvider = new Mock<IIssueTrackerProvider>();
        mockProvider.Setup(p => p.SourceType).Returns(ExternalSourceType.AzureDevOps);
        var service = CreateService(context, mockProvider.Object);

        var canPush = workItem.CanPushToExternal();
        canPush.Should().BeFalse("ClarificationRequested status must block outbound push even if ReviewStatus is NotRequired");

        var act = async () => await service.PushWorkItemToExternalAsync(workItem.Id);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Cannot push work item*");

        mockProvider.Verify(p => p.PushWorkItemAsync(
            It.IsAny<ProjectExternalConfig>(),
            It.IsAny<WorkItem>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }
}
