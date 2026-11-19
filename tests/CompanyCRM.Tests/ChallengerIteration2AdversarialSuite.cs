namespace CompanyCRM.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
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

/// <summary>
/// Empirical Challenger (Milestone 2, Iteration 2) Adversarial Test Suite.
/// Stress-tests canonical hashing delimiter collision resistance, outbound push gating invariants,
/// KeepBoth prefix deduplication, and financial precision.
/// </summary>
public class ChallengerIteration2AdversarialSuite : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<CrmDbContext> _options;
    private readonly AzureDevOpsSimulator _simulator;
    private readonly AzureDevOpsIssueTrackerProvider _provider;

    public ChallengerIteration2AdversarialSuite()
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

    private CrmDbContext CreateContext()
    {
        var context = new CrmDbContext(_options);
        context.Database.EnsureCreated();
        return context;
    }

    private SyncOrchestratorService CreateService(CrmDbContext context, IIssueTrackerProvider? provider = null)
    {
        return new SyncOrchestratorService(
            context,
            provider ?? _provider,
            NullLogger<SyncOrchestratorService>.Instance);
    }

    // =========================================================================
    // FOCUS 1: Canonical Content Hash Delimiter & Backslash Collisions
    // =========================================================================

    [Fact]
    public void Hasher_PipeShiftBetweenAdjacentFields_GuaranteesDistinctHashes()
    {
        // Challenge: Title="A|B", Desc="C" vs Title="A", Desc="B|C"
        var hash1 = SyncContentHasher.ComputeHash("A|B", "C", WorkItemStatus.Todo, WorkItemType.Task, "dev@crm.com");
        var hash2 = SyncContentHasher.ComputeHash("A", "B|C", WorkItemStatus.Todo, WorkItemType.Task, "dev@crm.com");

        hash1.Should().NotBe(hash2, "Pipe within title must not shift into description field");
    }

    [Fact]
    public void Hasher_LiteralBackslashPipeVsLiteralPipe_GuaranteesDistinctHashes()
    {
        // Challenge: Title=@"A\|B" vs Title="A|B"
        var hashWithEscapedPipe = SyncContentHasher.ComputeHash(@"A\|B", "C", WorkItemStatus.Todo, WorkItemType.Task, "dev@crm.com");
        var hashWithRawPipe = SyncContentHasher.ComputeHash("A|B", "C", WorkItemStatus.Todo, WorkItemType.Task, "dev@crm.com");

        hashWithEscapedPipe.Should().NotBe(hashWithRawPipe,
            "Literal backslash-pipe @" + "\"A\\|B\" must not collide with raw pipe \"A|B\"");
    }

    [Fact]
    public void Hasher_DoubleBackslashPipeVsSingleBackslashPipe_GuaranteesDistinctHashes()
    {
        // Challenge: Title=@"A\\|B" vs Title=@"A\|B"
        var hashDoubleBackslash = SyncContentHasher.ComputeHash(@"A\\|B", "C", WorkItemStatus.Todo, WorkItemType.Task, "dev@crm.com");
        var hashSingleBackslash = SyncContentHasher.ComputeHash(@"A\|B", "C", WorkItemStatus.Todo, WorkItemType.Task, "dev@crm.com");

        hashDoubleBackslash.Should().NotBe(hashSingleBackslash,
            "Double backslash pipe @" + "\"A\\\\|B\" must be distinct from single backslash pipe @" + "\"A\\|B\"");
    }

    [Fact]
    public void Hasher_TripleBackslashPipeVsDoubleBackslashPipe_GuaranteesDistinctHashes()
    {
        // Challenge: Title=@"A\\\|B" vs Title=@"A\\|B"
        var hashTriple = SyncContentHasher.ComputeHash(@"A\\\|B", "C", WorkItemStatus.Todo, WorkItemType.Task, "dev@crm.com");
        var hashDouble = SyncContentHasher.ComputeHash(@"A\\|B", "C", WorkItemStatus.Todo, WorkItemType.Task, "dev@crm.com");

        hashTriple.Should().NotBe(hashDouble,
            "Triple backslash pipe must be distinct from double backslash pipe");
    }

    [Theory]
    [InlineData(@"\", @"\\")]
    [InlineData(@"\\", @"\\\")]
    [InlineData(@"|", @"\|")]
    [InlineData(@"\|", @"\\|")]
    [InlineData(@"\\|", @"\\\|")]
    [InlineData(@"||", @"|")]
    [InlineData(@"|||", @"||")]
    public void Hasher_BackslashPipeCombinations_DistinctInputsProduceDistinctHashes(string titleA, string titleB)
    {
        var hashA = SyncContentHasher.ComputeHash(titleA, "SharedDesc", WorkItemStatus.Todo, WorkItemType.Task, "u@c.com");
        var hashB = SyncContentHasher.ComputeHash(titleB, "SharedDesc", WorkItemStatus.Todo, WorkItemType.Task, "u@c.com");

        hashA.Should().NotBe(hashB, $"Input '{titleA}' and '{titleB}' must yield distinct SHA-256 hashes");
    }

    [Fact]
    public void Hasher_EmailContainingPipeDelimiters_DoesNotCollideWithAdjacentFields()
    {
        // Challenge delimiter boundary between Type enum and Email
        var hash1 = SyncContentHasher.ComputeHash("Task", "Desc", WorkItemStatus.Todo, WorkItemType.Task, "user@crm.com|extra");
        var hash2 = SyncContentHasher.ComputeHash("Task", "Desc", WorkItemStatus.Todo, WorkItemType.Task, "user@crm.com");

        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public void Hasher_CombinatorialStressGenerator_ZeroCollisionsAcross500DistinctTuples()
    {
        // Generator creates permutations of tokens with edge delimiters
        var tokens = new[] { "A", "B", "A|B", @"A\|B", @"A\\|B", @"A\\\|B", @"\|", @"\\|", "||", "A\nB", "A\r\nB", "" };
        var statuses = new[] { WorkItemStatus.Draft, WorkItemStatus.Todo, WorkItemStatus.Done };
        var types = new[] { WorkItemType.Task, WorkItemType.Bug };

        var seenHashes = new Dictionary<string, (string Title, string Desc, WorkItemStatus Status, WorkItemType Type)>();

        int testCount = 0;
        foreach (var title in tokens)
        {
            foreach (var desc in tokens)
            {
                foreach (var status in statuses)
                {
                    foreach (var type in types)
                    {
                        var hash = SyncContentHasher.ComputeHash(title, desc, status, type, "test@company.com");
                        hash.Should().NotBeNullOrWhiteSpace();
                        hash.Length.Should().Be(64);

                        // If normalized representations are identical (e.g. CRLF vs LF), hashes can match.
                        // Otherwise, distinct normalized inputs MUST have distinct hashes.
                        var normalizedTitle = title.Trim();
                        var normalizedDesc = desc.Replace("\r\n", "\n").Trim();

                        if (seenHashes.TryGetValue(hash, out var existing))
                        {
                            var existingNormTitle = existing.Title.Trim();
                            var existingNormDesc = existing.Desc.Replace("\r\n", "\n").Trim();

                            existingNormTitle.Should().Be(normalizedTitle, "Hash collision between distinct titles!");
                            existingNormDesc.Should().Be(normalizedDesc, "Hash collision between distinct descriptions!");
                            existing.Status.Should().Be(status, "Hash collision across statuses!");
                            existing.Type.Should().Be(type, "Hash collision across types!");
                        }
                        else
                        {
                            seenHashes[hash] = (title, desc, status, type);
                        }

                        testCount++;
                    }
                }
            }
        }

        testCount.Should().BeGreaterThan(400, "Generator must have evaluated over 400 combinatorial tuples");
    }

    // =========================================================================
    // FOCUS 2: Outbound Push Gating Invariants
    // =========================================================================

    [Theory]
    [InlineData(WorkItemStatus.Draft, ReviewStatus.NotRequired, false)]
    [InlineData(WorkItemStatus.Draft, ReviewStatus.Approved, false)]
    [InlineData(WorkItemStatus.PendingReview, ReviewStatus.Pending, false)]
    [InlineData(WorkItemStatus.PendingReview, ReviewStatus.NotRequired, false)]
    [InlineData(WorkItemStatus.ClarificationRequested, ReviewStatus.ClarificationNeeded, false)]
    [InlineData(WorkItemStatus.ClarificationRequested, ReviewStatus.NotRequired, false)]
    [InlineData(WorkItemStatus.Denied, ReviewStatus.Denied, false)]
    [InlineData(WorkItemStatus.Denied, ReviewStatus.NotRequired, false)]
    [InlineData(WorkItemStatus.Todo, ReviewStatus.Pending, false)]
    [InlineData(WorkItemStatus.Todo, ReviewStatus.ClarificationNeeded, false)]
    [InlineData(WorkItemStatus.Todo, ReviewStatus.Denied, false)]
    [InlineData(WorkItemStatus.Todo, ReviewStatus.Approved, true)]
    [InlineData(WorkItemStatus.Todo, ReviewStatus.NotRequired, true)]
    [InlineData(WorkItemStatus.InProgress, ReviewStatus.Approved, true)]
    [InlineData(WorkItemStatus.Done, ReviewStatus.Approved, true)]
    public void WorkItem_CanPushToExternal_ExhaustiveStatusMatrix(WorkItemStatus status, ReviewStatus reviewStatus, bool expectedCanPush)
    {
        var item = new WorkItem
        {
            Status = status,
            ReviewStatus = reviewStatus
        };

        item.CanPushToExternal().Should().Be(expectedCanPush,
            $"Status={status} and ReviewStatus={reviewStatus} must yield CanPushToExternal() == {expectedCanPush}");
    }

    [Theory]
    [InlineData(WorkItemStatus.Draft)]
    [InlineData(WorkItemStatus.PendingReview)]
    [InlineData(WorkItemStatus.ClarificationRequested)]
    [InlineData(WorkItemStatus.Denied)]
    public async Task SyncOrchestrator_PushWorkItemToExternalAsync_BlockedStatusesThrowInvalidOperation(WorkItemStatus status)
    {
        using var context = CreateContext();

        var project = new Project
        {
            Code = "PRJ",
            Name = "Push Test",
            ExternalSource = ExternalSourceType.AzureDevOps,
            IsSyncEnabled = true
        };
        var employee = new Employee { FullName = "Dev", Email = "dev@crm.com", Department = "Eng", JobTitle = "Dev" };
        context.Projects.Add(project);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var workItem = new WorkItem
        {
            ProjectId = project.Id,
            CreatedById = employee.Id,
            Code = $"PRJ-{status}",
            Title = $"Item in {status}",
            Status = status,
            ReviewStatus = ReviewStatus.NotRequired // Even if ReviewStatus bypassed, Status must block
        };
        context.WorkItems.Add(workItem);
        await context.SaveChangesAsync();

        var mockProvider = new Mock<IIssueTrackerProvider>();
        mockProvider.Setup(p => p.SourceType).Returns(ExternalSourceType.AzureDevOps);
        var service = CreateService(context, mockProvider.Object);

        // Act & Assert
        var act = async () => await service.PushWorkItemToExternalAsync(workItem.Id);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Cannot push work item*");

        // Verify provider was never called
        mockProvider.Verify(p => p.PushWorkItemAsync(
            It.IsAny<ProjectExternalConfig>(),
            It.IsAny<WorkItem>(),
            It.IsAny<CancellationToken>()), Times.Never);

        // Verify database entity was not updated with sync state
        var reloaded = await context.WorkItems.FirstAsync(w => w.Id == workItem.Id);
        reloaded.LastSyncedAt.Should().BeNull();
        reloaded.LastSyncedContentHash.Should().BeNull();
        reloaded.ExternalId.Should().BeNull();
    }

    [Theory]
    [InlineData(ReviewStatus.Pending)]
    [InlineData(ReviewStatus.ClarificationNeeded)]
    [InlineData(ReviewStatus.Denied)]
    public async Task SyncOrchestrator_PushWorkItemToExternalAsync_BlockedReviewStatusesThrowInvalidOperation(ReviewStatus reviewStatus)
    {
        using var context = CreateContext();

        var project = new Project
        {
            Code = "PRJ2",
            Name = "Push Test 2",
            ExternalSource = ExternalSourceType.AzureDevOps,
            IsSyncEnabled = true
        };
        var employee = new Employee { FullName = "Dev", Email = "dev@crm.com", Department = "Eng", JobTitle = "Dev" };
        context.Projects.Add(project);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var workItem = new WorkItem
        {
            ProjectId = project.Id,
            CreatedById = employee.Id,
            Code = $"PRJ2-{reviewStatus}",
            Title = $"Item in review status {reviewStatus}",
            Status = WorkItemStatus.Todo, // Status is Todo, but ReviewStatus must block!
            ReviewStatus = reviewStatus
        };
        context.WorkItems.Add(workItem);
        await context.SaveChangesAsync();

        var mockProvider = new Mock<IIssueTrackerProvider>();
        mockProvider.Setup(p => p.SourceType).Returns(ExternalSourceType.AzureDevOps);
        var service = CreateService(context, mockProvider.Object);

        // Act & Assert
        var act = async () => await service.PushWorkItemToExternalAsync(workItem.Id);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Cannot push work item*");

        mockProvider.Verify(p => p.PushWorkItemAsync(
            It.IsAny<ProjectExternalConfig>(),
            It.IsAny<WorkItem>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    // =========================================================================
    // FOCUS 3: KeepBoth Double Prefixing Invariants
    // =========================================================================

    [Theory]
    [InlineData("ADO-5002", "ADO-5002")]
    [InlineData("ado-5002", "ado-5002")]
    [InlineData("Ado-5002", "Ado-5002")]
    [InlineData("12345", "ADO-12345")]
    [InlineData("9999", "ADO-9999")]
    [InlineData("EXT-100", "ADO-EXT-100")]
    [InlineData("ADO-ADO-777", "ADO-ADO-777")] // Literal input starting with ADO- must not gain another prefix
    public async Task SyncOrchestrator_ResolveConflict_KeepBoth_NeverProducesDoubleAdoPrefix(string externalId, string expectedCode)
    {
        using var context = CreateContext();

        var project = new Project
        {
            Code = "CRM",
            Name = "CompanyCRM",
            ExternalSource = ExternalSourceType.AzureDevOps,
            ExternalProjectName = "MyAdoProject",
            IsSyncEnabled = true
        };
        var employee = new Employee { FullName = "Dev", Email = "dev@company.com", Department = "Eng", JobTitle = "Dev" };
        context.Projects.Add(project);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var localItem = new WorkItem
        {
            ProjectId = project.Id,
            CreatedById = employee.Id,
            Code = "CRM-101",
            Title = "Local Existing Item",
            Status = WorkItemStatus.Todo
        };
        context.WorkItems.Add(localItem);
        await context.SaveChangesAsync();

        var conflict = new SyncConflict
        {
            ProjectId = project.Id,
            LocalWorkItemId = localItem.Id,
            ExternalSource = ExternalSourceType.AzureDevOps,
            ExternalId = externalId,
            ExternalTitle = $"Remote Task For {externalId}",
            ExternalDescription = "Desc from remote",
            ExternalStatus = "Active",
            Status = ConflictStatus.Pending,
            ConflictType = SyncConflictType.DuplicateCandidate
        };
        context.SyncConflicts.Add(conflict);
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var result = await service.ResolveConflictAsync(conflict.Id, ConflictResolutionAction.KeepBoth);

        result.Resolved.Should().BeTrue();

        // Reload the newly imported work item
        var importedItem = await context.WorkItems.FirstOrDefaultAsync(w => w.ExternalId == externalId);
        importedItem.Should().NotBeNull();
        importedItem!.Code.Should().Be(expectedCode, $"Conflict externalId '{externalId}' must produce Code '{expectedCode}'");
        importedItem.ExternalId.Should().Be(externalId, "ExternalId must retain raw remote ID unmodified");

        // Explicitly assert that no double ADO-ADO prefix was formed unless input literally had it
        if (!externalId.StartsWith("ADO-ADO-", StringComparison.OrdinalIgnoreCase))
        {
            importedItem.Code.Should().NotStartWith("ADO-ADO-", "Double ADO-ADO- prefix is strictly forbidden");
        }
    }

    [Fact]
    public async Task SyncOrchestrator_MultipleConsecutiveKeepBoth_HandlesAllCorrectly()
    {
        using var context = CreateContext();

        var project = new Project
        {
            Code = "MULTI",
            Name = "Multi Project",
            ExternalSource = ExternalSourceType.AzureDevOps,
            IsSyncEnabled = true
        };
        var employee = new Employee { FullName = "Dev", Email = "dev@multi.com", Department = "Eng", JobTitle = "Dev" };
        context.Projects.Add(project);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var testCases = new (string ExtId, string ExpectedCode)[]
        {
            ("ADO-111", "ADO-111"),
            ("222", "ADO-222"),
            ("ado-333", "ado-333"),
            ("444", "ADO-444")
        };

        var service = CreateService(context);

        foreach (var (extId, expectedCode) in testCases)
        {
            var conflict = new SyncConflict
            {
                ProjectId = project.Id,
                ExternalSource = ExternalSourceType.AzureDevOps,
                ExternalId = extId,
                ExternalTitle = $"Remote Item {extId}",
                ExternalStatus = "Active",
                Status = ConflictStatus.Pending,
                ConflictType = SyncConflictType.DuplicateCandidate
            };
            context.SyncConflicts.Add(conflict);
            await context.SaveChangesAsync();

            var res = await service.ResolveConflictAsync(conflict.Id, ConflictResolutionAction.KeepBoth);
            res.Resolved.Should().BeTrue();

            var imported = await context.WorkItems.FirstAsync(w => w.ExternalId == extId);
            imported.Code.Should().Be(expectedCode);
        }

        var allImported = await context.WorkItems.Where(w => w.ProjectId == project.Id).ToListAsync();
        allImported.Count.Should().Be(4);
        allImported.All(i => !i.Code.StartsWith("ADO-ADO-")).Should().BeTrue();
    }

    // =========================================================================
    // FOCUS 4: Financial Precision & Penny-Drift Adversarial Stress
    // =========================================================================

    [Fact]
    public void FinancialCalculator_AdversarialPennyDrift_ManyRepeatingDecimalEntries()
    {
        // 60 entries of 1 minute each at $100/hr = 60 * (1/60 * 100) = exactly $100.00
        // Per-entry rounding would do Math.Round(1.6666..., 2) = 1.67
        // 60 * 1.67 = $100.20 ($0.20 penny drift!)
        // Our remediated calculation sums unrounded values and rounds once at the end: exactly $100.00
        var entries = Enumerable.Range(0, 60)
            .Select(_ => (DurationMinutes: 1.0m, HourlyBillableRate: 100.00m, IsBillable: true))
            .ToList();

        var totalRevenue = FinancialCalculator.CalculateProjectRevenue(entries);
        totalRevenue.Should().Be(100.00m, "60 one-minute entries at $100/hr must total exactly $100.00 without penny-drift");
    }

    [Fact]
    public void FinancialCalculator_HighVolumeStress_10000Entries_ExecutesAccurately()
    {
        // 10,000 entries of 7 minutes at $123.45/hr
        // 70,000 / 60 * 123.45 = 144,025.00
        var entries = Enumerable.Range(0, 10000)
            .Select(_ => (DurationMinutes: 7.0m, HourlyBillableRate: 123.45m, IsBillable: true))
            .ToList();

        var totalRevenue = FinancialCalculator.CalculateProjectRevenue(entries);
        totalRevenue.Should().Be(144025.00m);
    }
}
