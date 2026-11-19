namespace CompanyCRM.Tests;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using FluentAssertions;
using Moq;
using CompanyCRM.Application.Services;
using CompanyCRM.Application.Interfaces;
using CompanyCRM.Application.DTOs;
using CompanyCRM.Domain.Entities;
using CompanyCRM.Domain.Enums;
using CompanyCRM.Tests.Common;

/// <summary>
/// Empirical Challenger 2 Adversarial Stress & Fuzzing Harness:
/// Rigorously tests SyncContentHasher, FinancialCalculator, and Reviewer Workflow Invariants.
/// </summary>
public class ChallengerE2ER2AdversarialTests
{
    // =========================================================================
    // SECTION 1: SyncContentHasher Fuzzing & Adversarial Stress Tests
    // =========================================================================

    [Theory]
    [InlineData("Line 1\r\nLine 2", "Line 1\nLine 2")]
    [InlineData("Line 1\r\n\r\nLine 2\r\n", "Line 1\n\nLine 2")]
    [InlineData("  Line 1\r\nLine 2  ", "Line 1\nLine 2")]
    [InlineData("\r\n\r\nTrim Test\r\n\r\n", "Trim Test")]
    public void Hasher_LineEndingAndWhitespacePermutations_ProducesIdenticalHash(string inputA, string inputB)
    {
        var hashA = SyncContentHasher.ComputeHash("Task Title", inputA, WorkItemStatus.Todo, WorkItemType.Task, "dev@crm.com");
        var hashB = SyncContentHasher.ComputeHash("Task Title", inputB, WorkItemStatus.Todo, WorkItemType.Task, "dev@crm.com");

        hashA.Should().Be(hashB, "SyncContentHasher must normalize CRLF to LF and trim leading/trailing whitespace");
    }

    [Fact]
    public void Hasher_StandaloneCrWithoutLf_BehavesDeterministically()
    {
        // Notice: Classical Mac line endings (\r without \n)
        var hashCr = SyncContentHasher.ComputeHash("Task", "Line1\rLine2", WorkItemStatus.Todo, WorkItemType.Task, "dev@crm.com");
        var hashLf = SyncContentHasher.ComputeHash("Task", "Line1\nLine2", WorkItemStatus.Todo, WorkItemType.Task, "dev@crm.com");

        // SyncContentHasher only replaces \r\n with \n; standalone \r is preserved as \r
        hashCr.Should().NotBeNullOrWhiteSpace();
        hashLf.Should().NotBeNullOrWhiteSpace();
        hashCr.Should().NotBe(hashLf, "Standalone CR is distinct from LF in UTF-8 encoding");
    }

    [Theory]
    [InlineData("   Leading space title", "Leading space title")]
    [InlineData("Trailing space title   ", "Trailing space title")]
    [InlineData("\t\tTabbed title\t", "Tabbed title")]
    [InlineData("  Title with tabs \t and spaces  ", "Title with tabs \t and spaces")]
    public void Hasher_TitleWhitespaceTrimming_NormalizesOuterWhitespace(string unnormalizedTitle, string cleanTitle)
    {
        var hashUnnormalized = SyncContentHasher.ComputeHash(unnormalizedTitle, "Desc", WorkItemStatus.Todo, WorkItemType.Task, "user@crm.com");
        var hashClean = SyncContentHasher.ComputeHash(cleanTitle, "Desc", WorkItemStatus.Todo, WorkItemType.Task, "user@crm.com");

        hashUnnormalized.Should().Be(hashClean, "Outer whitespace and tabs in title must be trimmed identically");
    }

    [Theory]
    [InlineData("john.doe@company.org", "JOHN.DOE@COMPANY.ORG")]
    [InlineData("John.Doe@Company.Org", "john.doe@company.org")]
    [InlineData("  John.Doe@Company.Org  ", "john.doe@company.org")]
    [InlineData("\t\tJOHN.DOE@COMPANY.ORG\t\r\n", "john.doe@company.org")]
    public void Hasher_EmailCasingAndWhitespace_NormalizesIdentically(string email1, string email2)
    {
        var hash1 = SyncContentHasher.ComputeHash("Fix Bug", "Description", WorkItemStatus.InProgress, WorkItemType.Bug, email1);
        var hash2 = SyncContentHasher.ComputeHash("Fix Bug", "Description", WorkItemStatus.InProgress, WorkItemType.Bug, email2);

        hash1.Should().Be(hash2, "Email casing and whitespace must be fully normalized to lowercase trimmed string");
    }

    [Theory]
    [InlineData("🚀 Launch Application to Prod", "Deploy to cluster 🔥 with 100% confidence")]
    [InlineData("ユーザー認証機能の不具合修正", "日本語のマルチバイト文字のテスト。")]
    [InlineData("Fehlerbehebung für Überweisungen", "Prüfung von Umlauten: ä, ö, ü, ß.")]
    [InlineData("تصحيح خطأ الخادم", "تحقق من صحة النص العربي ثنائي الاتجاه")]
    [InlineData("Complex ZWJ sequence: 👨‍👩‍👧‍👦 Family", "Multi-symbol ZWJ text representation")]
    public void Hasher_UnicodeAndEmojiContent_GeneratesValidSha256Hex(string title, string description)
    {
        var hash = SyncContentHasher.ComputeHash(title, description, WorkItemStatus.Done, WorkItemType.Story, "unicode@crm.internal");

        hash.Should().NotBeNullOrWhiteSpace();
        hash.Should().HaveLength(64);
        hash.Should().MatchRegex("^[A-F0-9]{64}$", "Sha256 hash must be 64-character uppercase hex");
    }

    [Fact]
    public void Hasher_UnicodeNfcVsNfdNormalization_DocumentsFormBehavior()
    {
        // NFC: composed 'é' (\u00E9)
        // NFD: decomposed 'e' + combining acute accent (\u0065\u0301)
        var titleNfc = "Caf\u00e9 Feature";
        var titleNfd = "Caf\u0065\u0301 Feature";

        var hashNfc = SyncContentHasher.ComputeHash(titleNfc, "Desc", WorkItemStatus.Todo, WorkItemType.Task, "dev@crm.com");
        var hashNfd = SyncContentHasher.ComputeHash(titleNfd, "Desc", WorkItemStatus.Todo, WorkItemType.Task, "dev@crm.com");

        // SyncContentHasher operates on raw UTF-8 bytes without string.Normalize();
        // UTF-8 bytes differ between NFC (C3 A9) and NFD (65 CC 81).
        hashNfc.Should().NotBe(hashNfd, "NFC and NFD produce distinct UTF-8 byte sequences unless normalized");
    }

    [Fact]
    public void Hasher_EchoPayloadFuzzing_Simulates100RoundsOfRemoteFormattingDifferences()
    {
        // Outbound CompanyCRM work item state
        var baseTitle = "Refactor Background Queue";
        var baseDesc = "Migrate Hangfire jobs to .NET Aspire distributed workers.\r\nEnsure idempotent execution.";
        var baseStatus = WorkItemStatus.InProgress;
        var baseType = WorkItemType.Task;
        var baseEmail = "lead@companycrm.com";

        var outboundHash = SyncContentHasher.ComputeHash(baseTitle, baseDesc, baseStatus, baseType, baseEmail);

        // Remote Azure DevOps webhook echo payloads with formatting variants:
        // CRLF vs LF, leading/trailing whitespace, uppercase email, extra internal newlines
        var variants = new[]
        {
            ("Refactor Background Queue", "Migrate Hangfire jobs to .NET Aspire distributed workers.\nEnsure idempotent execution.", "lead@companycrm.com"),
            ("  Refactor Background Queue  ", "Migrate Hangfire jobs to .NET Aspire distributed workers.\r\nEnsure idempotent execution.\r\n", "LEAD@COMPANYCRM.COM"),
            ("Refactor Background Queue", "  Migrate Hangfire jobs to .NET Aspire distributed workers.\nEnsure idempotent execution.  ", "  lead@companycrm.com  "),
            ("\tRefactor Background Queue\t", "\n\nMigrate Hangfire jobs to .NET Aspire distributed workers.\nEnsure idempotent execution.\n\n", "Lead@CompanyCRM.com"),
        };

        foreach (var (vTitle, vDesc, vEmail) in variants)
        {
            var echoHash = SyncContentHasher.ComputeHash(vTitle, vDesc, baseStatus, baseType, vEmail);
            echoHash.Should().Be(outboundHash,
                $"Echo payload with formatting variance (Title='{vTitle}', Email='{vEmail}') must match outbound hash");
        }
    }

    [Fact]
    public void Hasher_DelimiterCollision_DocumentsPipeSeparatorBehavior()
    {
        // Investigating pipe delimiter collision across adjacent fields:
        // Item 1: Title = "Task|Sub", Desc = "Details"
        // Item 2: Title = "Task", Desc = "Sub|Details"
        var hash1 = SyncContentHasher.ComputeHash("Task|Sub", "Details", WorkItemStatus.Todo, WorkItemType.Task, "user@crm.com");
        var hash2 = SyncContentHasher.ComputeHash("Task", "Sub|Details", WorkItemStatus.Todo, WorkItemType.Task, "user@crm.com");

        // Canonical format is: Title|Desc|Status|Type|Email
        // With field-level delimiter escaping, distinct field contents produce distinct canonical strings
        hash1.Should().NotBe(hash2, "Escaped pipe characters in adjacent fields prevent identical canonical strings");
    }

    [Fact]
    public void Hasher_BackslashAndPipeEscaping_PreventsBoundaryCollisions()
    {
        // Item 1: Title contains literal backslash and pipe @"Foo\|Bar", Desc = @"Baz"
        // Item 2: Title = @"Foo", Desc = @"\|Bar|Baz"
        var hash1 = SyncContentHasher.ComputeHash(@"Foo\|Bar", @"Baz", WorkItemStatus.Todo, WorkItemType.Task, "user@crm.com");
        var hash2 = SyncContentHasher.ComputeHash(@"Foo", @"\|Bar|Baz", WorkItemStatus.Todo, WorkItemType.Task, "user@crm.com");

        hash1.Should().NotBe(hash2, "Escaped backslashes and pipes ensure unique canonical representations");
    }

    // =========================================================================
    // SECTION 2: FinancialCalculator Adversarial Precision & Math Stress Tests
    // =========================================================================

    [Fact]
    public void Financial_FractionalMinutesAndHours_CalculatesExactRevenueAndCost()
    {
        // 1 minute = 1/60 hr ($150/hr rate -> $2.50)
        // 0.5 minutes (30 seconds) = 1/120 hr ($120/hr rate -> $1.00)
        // 13 minutes = 13/60 hr ($180/hr rate -> $39.00)
        var revenueEntries = new List<(decimal DurationMinutes, decimal HourlyBillableRate, bool IsBillable)>
        {
            (1.0m, 150.00m, true), // 1/60 * 150 = 2.50m
            (0.5m, 120.00m, true), // 0.5/60 * 120 = 1.00m
            (13.0m, 180.00m, true), // 13/60 * 180 = 39.00m
            (45.0m, 200.00m, false) // non-billable -> excluded from revenue
        };

        var revenue = FinancialCalculator.CalculateProjectRevenue(revenueEntries);
        revenue.Should().Be(42.50m, "Fractional minutes must compute exact billable revenue");

        var costEntries = new List<(decimal DurationMinutes, decimal HourlyCostRate)>
        {
            (1.0m, 60.00m),   // 1.00m
            (0.5m, 60.00m),   // 0.50m
            (13.0m, 60.00m),  // 13.00m
            (45.0m, 60.00m)   // 45.00m
        };

        var cost = FinancialCalculator.CalculateProjectInternalCost(costEntries);
        cost.Should().Be(59.50m, "Fractional minutes must compute exact internal cost across all entries");
    }

    [Fact]
    public void Financial_ZeroBillableEntries_ReturnsZeroRevenue_AndPreservesCost()
    {
        var entries = new List<(decimal DurationMinutes, decimal HourlyBillableRate, bool IsBillable)>
        {
            (120m, 150.00m, false),
            (60m, 200.00m, false),
            (30m, 100.00m, false)
        };

        var revenue = FinancialCalculator.CalculateProjectRevenue(entries);
        revenue.Should().Be(0.0m);

        var costEntries = entries.Select(e => (e.DurationMinutes, HourlyCostRate: 50.00m));
        var cost = FinancialCalculator.CalculateProjectInternalCost(costEntries);
        cost.Should().Be((210m / 60m) * 50.00m); // 3.5 hrs * $50 = $175.00m

        var (profit, margin) = FinancialCalculator.CalculateProfitability(revenue, cost);
        profit.Should().Be(-175.00m);
        margin.Should().Be(0.0m, "Zero revenue must safely return 0.00% margin without division-by-zero");
    }

    [Theory]
    [InlineData(1000.0, 1500.0, -500.0, -50.0)]     // Loss: -50% margin
    [InlineData(1000.0, 3000.0, -2000.0, -200.0)]   // Loss: -200% margin
    [InlineData(500.0, 500.0, 0.0, 0.0)]            // Break-even: 0% margin
    [InlineData(1000.0, 200.0, 800.0, 80.0)]        // Profit: 80% margin
    public void Financial_Profitability_ComputesNegativeAndPositiveMarginsCorrectly(
        double revenue, double cost, double expProfit, double expMargin)
    {
        var (profit, margin) = FinancialCalculator.CalculateProfitability((decimal)revenue, (decimal)cost);

        profit.Should().Be((decimal)expProfit);
        margin.Should().Be((decimal)expMargin);
    }

    [Theory]
    [InlineData(0.0, 0.0, 0.0)]
    [InlineData(0.0, 1000.0, 0.0)]
    [InlineData(40.0, 0.0, 0.0)]       // Division by zero capacity guard
    [InlineData(40.0, -40.0, 0.0)]     // Negative capacity guard
    [InlineData(30.0, 37.5, 80.0)]     // Fractional European 37.5h work week
    [InlineData(37.5, 37.5, 100.0)]    // 100% utilization on fractional capacity
    [InlineData(41.25, 37.5, 110.0)]   // Over-utilization on fractional capacity
    [InlineData(40.0, 1000.0, 4.0)]    // Extreme high capacity (1000h)
    [InlineData(1000.0, 40.0, 2500.0)] // Extreme high billable hours
    [InlineData(0.001, 40.0, 0.0)]     // Tiny fractional billable hours rounding
    public void Financial_Utilization_HandlesZeroFractionalAndExtremeCapacities(
        double billable, double capacity, double expectedPercent)
    {
        var result = FinancialCalculator.CalculateUtilizationPercent((decimal)billable, (decimal)capacity);
        result.Should().Be((decimal)expectedPercent);
    }

    [Theory]
    [InlineData(0, 0, 0, 0.0, 0.0)]
    [InlineData(-10, 5, 5, 0.0, 0.0)]
    [InlineData(20, 15, 12, 75.0, 60.0)]
    [InlineData(22, 22, 22, 100.0, 100.0)]
    public void Financial_PlanCompliance_GuardsAgainstZeroAndNegativeWorkingDays(
        int workingDays, int submitted, int onTime, double expCompliance, double expOnTime)
    {
        var (compliance, onTimeRate) = FinancialCalculator.CalculatePlanCompliance(workingDays, submitted, onTime);

        compliance.Should().Be((decimal)expCompliance);
        onTimeRate.Should().Be((decimal)expOnTime);
    }

    // =========================================================================
    // SECTION 3: Reviewer Workflow Invariant Fuzzing & Illegal Transitions
    // =========================================================================

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("ab")]
    [InlineData("abc")]
    [InlineData("abcd")]               // Exactly 4 ASCII characters -> MUST throw
    [InlineData(" 1234 ")]             // Exactly 4 characters trimmed -> MUST throw
    [InlineData("🚀")]                 // 1 Emoji (2 UTF-16 code units) -> Length 2 < 5 -> MUST throw
    [InlineData("🚀🔥")]               // 2 Emojis (4 UTF-16 code units) -> Length 4 < 5 -> MUST throw
    [InlineData("🚀ab")]               // 1 Emoji + 2 ASCII (4 UTF-16 code units) -> Length 4 < 5 -> MUST throw
    [InlineData("日本語で")]             // 4 CJK characters (4 UTF-16 code units) -> Length 4 < 5 -> MUST throw
    [InlineData("  🚀🔥  ")]           // 2 Emojis with whitespace -> trimmed Length 4 < 5 -> MUST throw
    public void Review_ClarificationAndDenial_LengthUnder5_ThrowsArgumentException(string? invalidComment)
    {
        var workItem = new WorkItem
        {
            Status = WorkItemStatus.PendingReview,
            ReviewStatus = ReviewStatus.Pending
        };
        var reviewerId = Guid.NewGuid();

        // Clarification check
        var actClarify = () => workItem.RequestClarification(reviewerId, invalidComment!);
        actClarify.Should().Throw<ArgumentException>()
            .WithMessage("*Clarification note must contain at least 5 characters.*");

        // Denial check
        var actDeny = () => workItem.DenyReview(reviewerId, invalidComment!);
        actDeny.Should().Throw<ArgumentException>()
            .WithMessage("*Denial reason must contain at least 5 characters.*");
    }

    [Theory]
    [InlineData("12345")]              // Exactly 5 ASCII characters -> MUST succeed
    [InlineData("abcde")]              // Exactly 5 characters -> MUST succeed
    [InlineData("  abcde  ")]          // Exactly 5 characters after trimming -> MUST succeed
    [InlineData("🚀🔥⚡")]             // 3 Emojis (6 UTF-16 code units) -> Length 6 >= 5 -> MUST succeed
    [InlineData("🚀abc")]              // 1 Emoji + 3 ASCII (5 UTF-16 code units) -> Length 5 >= 5 -> MUST succeed
    [InlineData("日本語です")]           // 5 CJK characters (5 UTF-16 code units) -> Length 5 >= 5 -> MUST succeed
    [InlineData("👨‍👩‍👧‍👦")]             // ZWJ Family emoji (11 UTF-16 code units) -> Length 11 >= 5 -> MUST succeed
    public void Review_ClarificationAndDenial_Length5OrMore_Succeeds(string validComment)
    {
        var workItem1 = new WorkItem { Status = WorkItemStatus.PendingReview, ReviewStatus = ReviewStatus.Pending };
        var workItem2 = new WorkItem { Status = WorkItemStatus.PendingReview, ReviewStatus = ReviewStatus.Pending };
        var reviewerId = Guid.NewGuid();

        workItem1.RequestClarification(reviewerId, validComment);
        workItem1.ReviewStatus.Should().Be(ReviewStatus.ClarificationNeeded);
        workItem1.ReviewLatestComment.Should().Be(validComment.Trim());

        workItem2.DenyReview(reviewerId, validComment);
        workItem2.ReviewStatus.Should().Be(ReviewStatus.Denied);
        workItem2.ReviewLatestComment.Should().Be(validComment.Trim());
    }

    [Fact]
    public async Task ReviewWorkflow_IllegalTransition_DeniedToApproved_DocumentsVulnerability()
    {
        // Setup in-memory DB and review service
        var db = TestDataBuilder.CreateInMemoryDbContext();
        var emailMock = new Mock<IEmailNotificationService>();
        var syncMock = new Mock<ISyncOrchestratorService>();

        var project = TestDataBuilder.CreateProject();
        var creator = TestDataBuilder.CreateEmployee(email: "alice@crm.com", fullName: "Alice", role: AppRole.Employee);
        var reviewer = TestDataBuilder.CreateEmployee(email: "bob@crm.com", fullName: "Bob", role: AppRole.TeamLead);
        db.Projects.Add(project);
        db.Employees.AddRange(creator, reviewer);

        var item = TestDataBuilder.CreateWorkItem(
            project.Id,
            creator.Id,
            code: "CRM-STRESS-1",
            title: "Denied Task",
            status: WorkItemStatus.Denied,
            reviewStatus: ReviewStatus.Denied,
            reviewerId: reviewer.Id);
        db.WorkItems.Add(item);
        await db.SaveChangesAsync();

        var service = new TestWorkItemReviewService(db, emailMock.Object, syncMock.Object, callerId: reviewer.Id);

        // STRESS TEST: Attempt to Approve a work item that was previously DENIED.
        // In the requirement: "Denying a review permanently blocks external synchronization."
        // A denied work item must NEVER be approved or synced to external trackers.
        var actApproveDenied = async () => await service.ApproveReviewAsync(item.Id, reviewer.Id, "Attempt override", CancellationToken.None);

        await actApproveDenied.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Cannot approve a denied work item*");

        // Sync must NEVER be invoked on a denied item
        syncMock.Verify(x => x.PushWorkItemToExternalAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReviewWorkflow_IllegalTransition_ApprovedToDraft_EvaluatesKanbanGating()
    {
        var db = TestDataBuilder.CreateInMemoryDbContext();
        var emailMock = new Mock<IEmailNotificationService>();
        var syncMock = new Mock<ISyncOrchestratorService>();

        var project = TestDataBuilder.CreateProject();
        var creator = TestDataBuilder.CreateEmployee(email: "alice@crm.com", fullName: "Alice", role: AppRole.Employee);
        var reviewer = TestDataBuilder.CreateEmployee(email: "bob@crm.com", fullName: "Bob", role: AppRole.TeamLead);
        db.Projects.Add(project);
        db.Employees.AddRange(creator, reviewer);

        var item = TestDataBuilder.CreateWorkItem(
            project.Id,
            creator.Id,
            code: "CRM-STRESS-2",
            title: "Approved Task",
            status: WorkItemStatus.Todo,
            reviewStatus: ReviewStatus.Approved,
            reviewerId: reviewer.Id);
        db.WorkItems.Add(item);
        await db.SaveChangesAsync();

        var service = new TestWorkItemReviewService(db, emailMock.Object, syncMock.Object, callerId: reviewer.Id);

        // STRESS TEST: Can an Approved item be regressed to Draft status via UpdateWorkItemStatusAsync?
        // Since CanTransitionKanban() allows transitions when ReviewStatus == Approved,
        // UpdateWorkItemStatusAsync sets Status to Draft without checking valid Kanban destination columns.
        var updated = await service.UpdateWorkItemStatusAsync(item.Id, WorkItemStatus.Draft, CancellationToken.None);

        updated.Status.Should().Be(WorkItemStatus.Draft);
        updated.ReviewStatus.Should().Be(ReviewStatus.Approved);
    }

    [Fact]
    public async Task ReviewWorkflow_DoubleApproval_IsProperlyGuarded()
    {
        var db = TestDataBuilder.CreateInMemoryDbContext();
        var emailMock = new Mock<IEmailNotificationService>();
        var syncMock = new Mock<ISyncOrchestratorService>();

        var project = TestDataBuilder.CreateProject();
        var creator = TestDataBuilder.CreateEmployee(email: "alice@crm.com", fullName: "Alice", role: AppRole.Employee);
        var reviewer = TestDataBuilder.CreateEmployee(email: "bob@crm.com", fullName: "Bob", role: AppRole.TeamLead);
        db.Projects.Add(project);
        db.Employees.AddRange(creator, reviewer);

        var item = TestDataBuilder.CreateWorkItem(
            project.Id,
            creator.Id,
            code: "CRM-STRESS-3",
            title: "Approved Task Double Approval",
            status: WorkItemStatus.Todo,
            reviewStatus: ReviewStatus.Approved,
            reviewerId: reviewer.Id);
        db.WorkItems.Add(item);
        await db.SaveChangesAsync();

        var service = new TestWorkItemReviewService(db, emailMock.Object, syncMock.Object, callerId: reviewer.Id);

        // Re-approving already approved item MUST throw InvalidOperationException
        var act = async () => await service.ApproveReviewAsync(item.Id, reviewer.Id, "Second approval", CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already*approved*");
    }

    [Theory]
    [InlineData(WorkItemStatus.PendingReview, ReviewStatus.Pending, false)]
    [InlineData(WorkItemStatus.ClarificationRequested, ReviewStatus.ClarificationNeeded, false)]
    [InlineData(WorkItemStatus.Denied, ReviewStatus.Denied, false)]
    [InlineData(WorkItemStatus.Todo, ReviewStatus.Approved, true)]
    [InlineData(WorkItemStatus.InProgress, ReviewStatus.Approved, true)]
    [InlineData(WorkItemStatus.Done, ReviewStatus.Approved, true)]
    [InlineData(WorkItemStatus.Backlog, ReviewStatus.NotRequired, true)]
    public void WorkItem_CanPushToExternalAndKanban_EnforcesStateInvariantsEmpirically(
        WorkItemStatus status, ReviewStatus reviewStatus, bool expectedCanTransition)
    {
        var item = new WorkItem
        {
            Status = status,
            ReviewStatus = reviewStatus
        };

        item.CanTransitionKanban().Should().Be(expectedCanTransition);
        item.CanPushToExternal().Should().Be(expectedCanTransition);
    }
}
