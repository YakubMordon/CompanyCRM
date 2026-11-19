namespace CompanyCRM.Tests;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CompanyCRM.Application.DTOs;
using CompanyCRM.Application.Services;
using CompanyCRM.Domain.Entities;
using CompanyCRM.Domain.Enums;
using CompanyCRM.Domain.ValueObjects;
using CompanyCRM.Infrastructure.ExternalSync;
using CompanyCRM.Infrastructure.Persistence;
using CompanyCRM.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

/// <summary>
/// Milestone 5 Phase 2 (Tier 5) Adversarial Coverage Hardening — Challenger 1.
/// Empirically probes financial precision, rounding, zero/negative boundaries,
/// webhook injection, large payload / algorithmic DoS, SHA-256 echo-loop resilience,
/// and conflict deduplication edge cases.
/// </summary>
public class ChallengerTier5FinancialAndSyncTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<CrmDbContext> _options;
    private readonly AzureDevOpsSimulator _simulator;
    private readonly AzureDevOpsIssueTrackerProvider _provider;

    public ChallengerTier5FinancialAndSyncTests()
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

    private CrmDbContext CreateSqliteContext()
    {
        var context = new CrmDbContext(_options);
        context.Database.EnsureCreated();
        return context;
    }

    // =========================================================================
    // FOCUS 1: Financial Calculation Precision, Rounding, Zero/Negative & Margin
    // =========================================================================

    [Fact]
    public void Financial_MidpointRounding_ProfitabilityAndUtilization_ShouldMatchAwayFromZeroStandard()
    {
        // FinancialCalculator.CalculateProjectRevenue and CalculateProjectInternalCost
        // explicitly use MidpointRounding.AwayFromZero.
        // CalculateProfitability and CalculateUtilizationPercent omit MidpointRounding (defaulting to ToEven).
        // Case 1: Revenue = 200.00m, Cost = 189.99m => Profit = 10.01m => Margin = (10.01 / 200) * 100 = 5.005%
        // AwayFromZero expects 5.01m; ToEven yields 5.00m.
        var (_, marginPercent) = FinancialCalculator.CalculateProfitability(200.00m, 189.99m);
        marginPercent.Should().Be(5.01m, "financial percentages should round consistently with MidpointRounding.AwayFromZero");

        // Case 2: BillableHours = 1.002m, Capacity = 40.0m => (1.002 / 40) * 100 = 2.505%
        var utilization = FinancialCalculator.CalculateUtilizationPercent(1.002m, 40.0m);
        utilization.Should().Be(2.51m, "utilization percentage should round consistently with MidpointRounding.AwayFromZero");
    }

    [Fact]
    public async Task Financial_ProjectProfitability_EmployeeBreakdownSum_ShouldReconcileWithProjectTotals()
    {
        using var context = CreateSqliteContext();

        var project = new Project { Code = "PEN", Name = "Penny Drift Probe" };
        context.Projects.Add(project);

        // 3 employees each log 20 minutes @ $100/hr billable and $50/hr cost.
        // Per-employee exact revenue = 20/60 * 100 = $33.3333... (rounded = $33.33)
        // Sum of 3 rounded employee breakdowns = $99.99, whereas project total = $100.00 ($0.01 penny drift!).
        var employees = Enumerable.Range(1, 3).Select(i => new Employee
        {
            FullName = $"Emp {i}",
            Email = $"emp{i}@penny.local",
            Department = "Eng",
            JobTitle = "Dev",
            HourlyCostRate = 50.00m,
            HourlyBillableRate = 100.00m
        }).ToList();
        context.Employees.AddRange(employees);

        var workItem = new WorkItem
        {
            ProjectId = project.Id,
            CreatedById = employees[0].Id,
            Code = "PEN-1",
            Title = "Shared task"
        };
        context.WorkItems.Add(workItem);

        foreach (var emp in employees)
        {
            context.TimeEntries.Add(new TimeEntry
            {
                WorkItemId = workItem.Id,
                EmployeeId = emp.Id,
                DurationMinutes = 20,
                IsBillable = true,
                IsRunning = false,
                LoggedDate = DateOnly.FromDateTime(DateTime.UtcNow)
            });
        }
        await context.SaveChangesAsync();

        var service = new ManagementAnalyticsService(context);
        var profitability = await service.GetProjectProfitabilityAsync(project.Id);

        profitability.TotalRevenue.Should().Be(100.00m);
        profitability.TotalInternalCost.Should().Be(50.00m);
        profitability.EmployeeBreakdowns.Sum(b => b.Revenue).Should().Be(
            profitability.TotalRevenue,
            "sum of employee revenue breakdowns should equal project total revenue without penny-drift");
        profitability.EmployeeBreakdowns.Sum(b => b.InternalCost).Should().Be(
            profitability.TotalInternalCost,
            "sum of employee internal cost breakdowns should equal project total cost without penny-drift");
    }

    [Fact]
    public async Task Financial_EmployeeCompensation_Utilization_ShouldNotUsePreRoundedHours()
    {
        using var context = CreateSqliteContext();

        var emp = new Employee
        {
            FullName = "Precision User",
            Email = "precision@crm.local",
            Department = "Eng",
            JobTitle = "Dev",
            WeeklyCapacityHours = 40.0m,
            HourlyCostRate = 50.00m,
            HourlyBillableRate = 100.00m
        };
        var project = new Project { Code = "UTL", Name = "Util Probe" };
        var item = new WorkItem { ProjectId = project.Id, CreatedById = emp.Id, Code = "UTL-1", Title = "Work" };
        // 1 minute billable = 0.016666... hours.
        // Exact utilization = (1 / 60 / 40) * 100 = 0.041666...% => 0.04%.
        // Pre-rounding hours to 0.02 first yields (0.02 / 40) * 100 = 0.05% (25% relative distortion).
        var entry = new TimeEntry
        {
            WorkItemId = item.Id,
            EmployeeId = emp.Id,
            DurationMinutes = 1,
            IsBillable = true,
            IsRunning = false,
            LoggedDate = DateOnly.FromDateTime(DateTime.UtcNow)
        };

        context.Employees.Add(emp);
        context.Projects.Add(project);
        context.WorkItems.Add(item);
        context.TimeEntries.Add(entry);
        await context.SaveChangesAsync();

        var service = new ManagementAnalyticsService(context);
        var comps = await service.GetEmployeeCompensationsAsync();
        var comp = comps.Single(c => c.EmployeeId == emp.Id);

        comp.ActualUtilizationPercent.Should().Be(0.04m,
            "utilization should be computed from exact minutes before rounding to avoid 25% error on short entries");
    }

    [Fact]
    public void Financial_ZeroAndNegativeInputs_BoundariesAndGuards()
    {
        // Zero revenue with positive cost: no DivideByZero, negative profit, 0% margin
        var (profitZeroRev, marginZeroRev) = FinancialCalculator.CalculateProfitability(0.00m, 1234.56m);
        profitZeroRev.Should().Be(-1234.56m);
        marginZeroRev.Should().Be(0.00m);

        // Negative revenue: guarded by revenue > 0.0m, returns 0% margin
        var (profitNegRev, marginNegRev) = FinancialCalculator.CalculateProfitability(-500.00m, 200.00m);
        profitNegRev.Should().Be(-700.00m);
        marginNegRev.Should().Be(0.00m);

        // Zero and negative capacity hours: guarded by totalCapacityHours <= 0.0m
        FinancialCalculator.CalculateUtilizationPercent(40.0m, 0.0m).Should().Be(0.00m);
        FinancialCalculator.CalculateUtilizationPercent(40.0m, -40.0m).Should().Be(0.00m);

        // 100%+ utilization (overtime)
        FinancialCalculator.CalculateUtilizationPercent(60.0m, 40.0m).Should().Be(150.00m);

        // Negative hours or negative rates passed to FinancialCalculator should not produce negative revenue/cost silently
        var negDurationEntries = new[] { (-60m, 100m, true) };
        var revFromNeg = FinancialCalculator.CalculateProjectRevenue(negDurationEntries);
        revFromNeg.Should().BeGreaterThanOrEqualTo(0m, "negative duration minutes should not generate negative revenue");

        var utilFromNeg = FinancialCalculator.CalculateUtilizationPercent(-10m, 40m);
        utilFromNeg.Should().BeGreaterThanOrEqualTo(0m, "negative billable hours should not yield negative utilization %");
    }

    [Fact]
    public void Financial_TimeEntryStopTimer_WhenClockSkewsBackwards_ClampsToMinimumOneMinute()
    {
        var start = DateTimeOffset.UtcNow;
        var entry = new TimeEntry
        {
            StartedAt = start,
            IsRunning = true
        };

        // Simulate clock skew where EndedAt is 10 minutes earlier than StartedAt
        entry.StopTimer(start.AddMinutes(-10));

        entry.IsRunning.Should().BeFalse();
        entry.DurationMinutes.Should().Be(1, "StopTimer clamps negative elapsed duration to minimum 1 minute");
    }

    // =========================================================================
    // FOCUS 2: Azure DevOps Sync — Webhook Injection, Large Payload, Collisions, Deduplication
    // =========================================================================

    [Theory]
    [InlineData("[]")]
    [InlineData("12345")]
    [InlineData("\"just a string\"")]
    [InlineData("true")]
    [InlineData("null")]
    [InlineData("{\"resource\": null}")]
    [InlineData("{\"resource\": []}")]
    [InlineData("{\"resource\": \"string\"}")]
    [InlineData("{\"resource\": {\"id\": 101, \"fields\": {\"System.Title\": 12345}}}")]
    [InlineData("{\"resource\": {\"id\": 101, \"fields\": {\"System.Title\": \"Valid\", \"System.Description\": [1, 2]}}}")]
    [InlineData("{\"resource\": {\"id\": 101, \"fields\": {\"System.Title\": \"Valid\", \"System.State\": {}}}}")]
    [InlineData("{\"resource\": {\"id\": 101, \"fields\": {\"System.Title\": \"Valid\", \"System.AssignedTo\": 999}}}")]
    public async Task Webhook_MalformedJsonTypesAndStructuralInjection_ShouldNotThrowUnhandledException(string rawJson)
    {
        // ParseWebhookPayloadAsync must handle any valid JSON with unexpected types gracefully (return null),
        // NOT throw InvalidOperationException (which causes HTTP 500 on POST /api/webhooks/ado).
        Func<Task> act = async () =>
        {
            var result = await _provider.ParseWebhookPayloadAsync(rawJson, new Dictionary<string, string>());
            // Either null or a parsed DTO with safe string defaults is acceptable; throwing is NOT.
            _ = result;
        };

        await act.Should().NotThrowAsync(
            $"malformed webhook payload '{rawJson}' must not crash ParseWebhookPayloadAsync with an unhandled exception");
    }

    [Fact]
    public async Task SyncWebhook_IntegrationEndpoint_NonObjectJsonPayload_ShouldReturnErrorNot500()
    {
        using var fixture = new CrmApiTestFixture();
        var client = fixture.CreateClient();

        var payloads = new[] { "[]", "null", "{\"resource\": null}", "{\"resource\":{\"id\":1,\"fields\":{\"System.Title\":123}}}" };
        foreach (var payload in payloads)
        {
            var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var response = await client.PostAsync("/api/webhooks/ado", content);

            ((int)response.StatusCode).Should().BeLessThan(500,
                $"POST /api/webhooks/ado with payload '{payload}' must not crash with HTTP 500 Internal Server Error");
        }
    }

    [Fact]
    public void Sync_ComputeTitleSimilarity_LargeStrings_ShouldNotAllocateQuadraticMatrix()
    {
        // Adversarial probe: two 10,000-char strings.
        // Current implementation allocates `new int[10001, 10001]` (~400 MB) for a single comparison!
        // With 45,000-char strings it would allocate ~8.1 GB and crash with OutOfMemoryException.
        var s1 = new string('a', 10_000) + " alpha";
        var s2 = new string('a', 10_000) + " beta";

        long memBefore = GC.GetTotalMemory(true);
        var sw = Stopwatch.StartNew();
        var sim = SyncOrchestratorService.ComputeTitleSimilarity(s1, s2);
        sw.Stop();
        long memAfter = GC.GetTotalMemory(false);
        long allocatedBytes = memAfter - memBefore;

        sim.Should().BeGreaterThan(0.9);
        // Rolling 1D Levenshtein uses ~80 KB for 10,000 chars; 2D int[,] allocates >400 MB!
        allocatedBytes.Should().BeLessThan(10 * 1024 * 1024,
            $"ComputeTitleSimilarity allocated {allocatedBytes / (1024.0 * 1024.0):F1} MB for 10k-char strings due to O(m*n) 2D matrix allocation (OOM DoS vector)");
    }

    [Fact]
    public async Task Sync_LiveProviderPushAndEchoWebhook_TitlePrefixMismatch_CausesEchoLoopFailure()
    {
        // Reproduces live-mode echo loop bug:
        // 1. SyncOrchestratorService computes LastSyncedContentHash using `item.Title` (without Code prefix).
        // 2. AzureDevOpsIssueTrackerProvider.PushWorkItemAsync (live mode) sends `System.Title = $"{item.Code}: {item.Title}"`.
        // 3. ADO returns the created item and fires an inbound webhook with `System.Title = $"{item.Code}: {item.Title}"`.
        // 4. SyncOrchestratorService hashes `parsedDto.Title` (`"CRM-101: Real Title"`) != `LastSyncedContentHash` (`"Real Title"`).
        // 5. Echo detection FAILS and mutates local title!
        using var context = CreateSqliteContext();
        var project = new Project
        {
            Code = "LIVE",
            Name = "Live ADO Project",
            ExternalSource = ExternalSourceType.AzureDevOps,
            ExternalOrgUrl = "https://dev.azure.com/liveorg",
            ExternalProjectName = "LiveProj",
            ExternalPat = "fake-live-pat",
            IsSyncEnabled = true,
            IsSimulatedMode = false
        };
        var emp = new Employee { FullName = "Dev", Email = "dev@live.local", Department = "Eng", JobTitle = "Dev" };
        context.Projects.Add(project);
        context.Employees.Add(emp);

        var item = new WorkItem
        {
            ProjectId = project.Id,
            CreatedById = emp.Id,
            AssigneeId = emp.Id,
            Code = "LIVE-101",
            Title = "Implement Live Webhook Echo Guard",
            Description = "Details",
            Status = WorkItemStatus.Todo,
            Type = WorkItemType.Task,
            ReviewStatus = ReviewStatus.Approved
        };
        context.WorkItems.Add(item);
        await context.SaveChangesAsync();

        string? capturedSentTitle = null;
        var handler = new StubHttpMessageHandler(async req =>
        {
            var body = await req.Content!.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            foreach (var op in doc.RootElement.EnumerateArray())
            {
                if (op.GetProperty("path").GetString() == "/fields/System.Title")
                {
                    capturedSentTitle = op.GetProperty("value").GetString();
                }
            }

            var responsePayload = JsonSerializer.Serialize(new
            {
                id = 7777,
                _links = new { html = new { href = "https://dev.azure.com/liveorg/LiveProj/_workitems/edit/7777" } },
                fields = new Dictionary<string, object?>
                {
                    ["System.Title"] = capturedSentTitle,
                    ["System.Description"] = item.Description,
                    ["System.State"] = "To Do",
                    ["System.WorkItemType"] = "Task",
                    ["System.AssignedTo"] = new { uniqueName = emp.Email }
                }
            });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responsePayload, Encoding.UTF8, "application/json")
            };
        });

        var liveProvider = new AzureDevOpsIssueTrackerProvider(
            new HttpClient(handler),
            _simulator,
            NullLogger<AzureDevOpsIssueTrackerProvider>.Instance);
        var orchestrator = new SyncOrchestratorService(context, liveProvider, NullLogger<SyncOrchestratorService>.Instance);

        var pushRes = await orchestrator.PushWorkItemToExternalAsync(item.Id);
        pushRes.Success.Should().BeTrue();

        // Simulate ADO echoing back the exact item it just received
        var echoWebhook = JsonSerializer.Serialize(new
        {
            eventType = "workitem.updated",
            resource = new
            {
                id = 7777,
                url = "https://dev.azure.com/liveorg/LiveProj/_workitems/edit/7777",
                fields = new Dictionary<string, object?>
                {
                    ["System.Title"] = capturedSentTitle,
                    ["System.Description"] = item.Description,
                    ["System.State"] = "To Do",
                    ["System.WorkItemType"] = "Task",
                    ["System.AssignedTo"] = new { uniqueName = emp.Email }
                }
            }
        });

        var webhookRes = await orchestrator.ProcessInboundWebhookAsync(
            ExternalSourceType.AzureDevOps,
            echoWebhook,
            new Dictionary<string, string>());

        webhookRes.IsEcho.Should().BeTrue("an immediate ADO echo of the item just pushed in live mode must be ignored as an echo");
        webhookRes.Status.Should().Be("IgnoredEcho");
    }

    [Fact]
    public async Task Sync_BacklogWorkItemPushAndEchoWebhook_StatusRoundTrip_CausesEchoLoopFailure()
    {
        // WorkItem.CanPushToExternal() allows WorkItemStatus.Backlog.
        // MapStatusToAdoState(Backlog) -> "New", but MapAdoStateToStatus("New") -> WorkItemStatus.Todo!
        // Therefore pushing a Backlog item and receiving its exact echo mutates it to Todo.
        using var context = CreateSqliteContext();
        var project = new Project
        {
            Code = "BKL",
            Name = "Backlog Sync Test",
            ExternalSource = ExternalSourceType.AzureDevOps,
            IsSyncEnabled = true,
            IsSimulatedMode = true
        };
        var emp = new Employee { FullName = "Dev", Email = "dev@bkl.local", Department = "Eng", JobTitle = "Dev" };
        context.Projects.Add(project);
        context.Employees.Add(emp);

        var item = new WorkItem
        {
            ProjectId = project.Id,
            CreatedById = emp.Id,
            AssigneeId = emp.Id,
            Code = "BKL-101",
            Title = "Approved Backlog Item",
            Description = "Waiting in backlog",
            Status = WorkItemStatus.Backlog,
            Type = WorkItemType.Task,
            ReviewStatus = ReviewStatus.Approved
        };
        context.WorkItems.Add(item);
        await context.SaveChangesAsync();

        var orchestrator = new SyncOrchestratorService(context, _provider, NullLogger<SyncOrchestratorService>.Instance);
        var pushRes = await orchestrator.PushWorkItemToExternalAsync(item.Id);
        pushRes.Success.Should().BeTrue();

        var remoteItems = await _provider.FetchRemoteWorkItemsAsync(
            new ProjectExternalConfig(project.Id, project.ExternalSource, project.ExternalOrgUrl, project.ExternalProjectName, null, true, true, null));
        var pushedRemote = remoteItems.Single(r => r.ExternalId == pushRes.ExternalId);
        var echoPayload = _simulator.GenerateServiceHookPayload(pushedRemote);

        var webhookRes = await orchestrator.ProcessInboundWebhookAsync(
            ExternalSourceType.AzureDevOps,
            echoPayload,
            new Dictionary<string, string>());

        webhookRes.IsEcho.Should().BeTrue("pushing an approved Backlog item and receiving its unmodified simulator echo must be recognized as IgnoredEcho");
    }

    [Fact]
    public void SyncContentHasher_CollisionResistance_ControlCharsUnicodeAndNulls()
    {
        // Verify distinct hashes for tricky control characters & boundary cases
        var hCrLf = SyncContentHasher.ComputeHash("Title", "Line1\r\nLine2", WorkItemStatus.Todo, WorkItemType.Task, "a@b.com");
        var hLf = SyncContentHasher.ComputeHash("Title", "Line1\nLine2", WorkItemStatus.Todo, WorkItemType.Task, "a@b.com");
        hCrLf.Should().Be(hLf, "CRLF and LF in description must normalize identically");

        var hNullByte1 = SyncContentHasher.ComputeHash("A\0B", "C", WorkItemStatus.Todo, WorkItemType.Task, "a@b.com");
        var hNullByte2 = SyncContentHasher.ComputeHash("A", "B\0C", WorkItemStatus.Todo, WorkItemType.Task, "a@b.com");
        hNullByte1.Should().NotBe(hNullByte2);

        var hEmptyTitle = SyncContentHasher.ComputeHash("", "A|B", WorkItemStatus.Todo, WorkItemType.Task, null);
        var hTitlePipe = SyncContentHasher.ComputeHash("A", "B", WorkItemStatus.Todo, WorkItemType.Task, null);
        hEmptyTitle.Should().NotBe(hTitlePipe);

        // 10,000 randomized adversarial strings with pipes, backslashes, newlines, unicode
        var rng = new Random(42);
        var chars = new[] { 'a', 'b', '|', '\\', '\n', ' ', '\t', 'é', '中' };
        var seen = new Dictionary<string, string>();
        for (int i = 0; i < 10_000; i++)
        {
            string RandStr() => new string(Enumerable.Range(0, rng.Next(0, 12)).Select(_ => chars[rng.Next(chars.Length)]).ToArray());
            var t = RandStr();
            var d = RandStr();
            var e = RandStr();
            var key = $"{t.Trim()}\u001F{d.Replace("\r\n", "\n").Trim()}\u001F{e.Trim().ToLowerInvariant()}";
            var hash = SyncContentHasher.ComputeHash(t, d, WorkItemStatus.InProgress, WorkItemType.Story, e);
            if (seen.TryGetValue(hash, out var existingKey))
            {
                existingKey.Should().Be(key, "SHA-256 canonical hash collided between two distinct normalized inputs!");
            }
            else
            {
                seen[hash] = key;
            }
        }
    }

    [Fact]
    public async Task ConflictDeduplication_SkipIdempotency_AcrossMultipleSyncEnabledProjects()
    {
        // When there are 2 ADO-sync-enabled projects (like seeded CRM & MOB),
        // ProcessInboundWebhookAsync picks FirstOrDefaultAsync(p => p.ExternalSource == source && p.IsSyncEnabled)
        // and checks Skip ONLY for that single project's ID!
        // Testing both projects deterministically guarantees reproducing the cross-project Skip bypass.
        using var context = CreateSqliteContext();

        var project1 = new Project
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            Code = "PRJ1",
            Name = "First Project",
            ExternalSource = ExternalSourceType.AzureDevOps,
            IsSyncEnabled = true,
            IsSimulatedMode = true
        };
        var project2 = new Project
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000002"),
            Code = "PRJ2",
            Name = "Second Project",
            ExternalSource = ExternalSourceType.AzureDevOps,
            IsSyncEnabled = true,
            IsSimulatedMode = true
        };
        var emp = new Employee { FullName = "Dev", Email = "dev@multi.local", Department = "Eng", JobTitle = "Dev" };
        context.Projects.AddRange(project1, project2);
        context.Employees.Add(emp);
        await context.SaveChangesAsync();

        context.SyncConflicts.AddRange(
            new SyncConflict
            {
                ProjectId = project1.Id,
                ExternalSource = ExternalSourceType.AzureDevOps,
                ExternalId = "ADO-SKIP-P1",
                ExternalTitle = "Skipped Remote Item In Project 1",
                ExternalStatus = "New",
                Status = ConflictStatus.Resolved,
                ResolutionAction = ConflictResolutionAction.Skip,
                ResolvedAt = DateTimeOffset.UtcNow
            },
            new SyncConflict
            {
                ProjectId = project2.Id,
                ExternalSource = ExternalSourceType.AzureDevOps,
                ExternalId = "ADO-SKIP-P2",
                ExternalTitle = "Skipped Remote Item In Project 2",
                ExternalStatus = "New",
                Status = ConflictStatus.Resolved,
                ResolutionAction = ConflictResolutionAction.Skip,
                ResolvedAt = DateTimeOffset.UtcNow
            });
        await context.SaveChangesAsync();

        var orchestrator = new SyncOrchestratorService(context, _provider, NullLogger<SyncOrchestratorService>.Instance);

        foreach (var extId in new[] { "ADO-SKIP-P1", "ADO-SKIP-P2" })
        {
            var payload = _simulator.GenerateServiceHookPayload(new ExternalWorkItemDto(
                extId,
                $"https://dev.azure.com/org/p/_workitems/edit/{extId}",
                $"Skipped Remote Item {extId}",
                "Desc",
                "New",
                "Task",
                null));

            var result = await orchestrator.ProcessInboundWebhookAsync(
                ExternalSourceType.AzureDevOps,
                payload,
                new Dictionary<string, string>());

            result.Status.Should().Be("Skipped",
                $"ExternalId '{extId}' was resolved as Skip and must not be imported into another sync-enabled project");
            (await context.WorkItems.AnyAsync(w => w.ExternalId == extId)).Should().BeFalse();
        }
    }

    [Fact]
    public async Task Sync_InboundWebhook_UnassignedRemoteItem_ClearsLocalAssigneeToPreventHashDrift()
    {
        using var context = CreateSqliteContext();
        var project = new Project
        {
            Code = "ASN",
            Name = "Assignee Drift Project",
            ExternalSource = ExternalSourceType.AzureDevOps,
            IsSyncEnabled = true,
            IsSimulatedMode = true
        };
        var emp = new Employee { FullName = "Dev", Email = "dev@asn.local", Department = "Eng", JobTitle = "Dev" };
        context.Projects.Add(project);
        context.Employees.Add(emp);

        var initialHash = SyncContentHasher.ComputeHash("Task A", "Desc", WorkItemStatus.Todo, WorkItemType.Task, emp.Email);
        var item = new WorkItem
        {
            ProjectId = project.Id,
            CreatedById = emp.Id,
            AssigneeId = emp.Id,
            Code = "ASN-1",
            Title = "Task A",
            Description = "Desc",
            Status = WorkItemStatus.Todo,
            Type = WorkItemType.Task,
            ReviewStatus = ReviewStatus.Approved,
            ExternalSource = ExternalSourceType.AzureDevOps,
            ExternalId = "ADO-ASN-1",
            LastSyncedContentHash = initialHash
        };
        context.WorkItems.Add(item);
        await context.SaveChangesAsync();

        var orchestrator = new SyncOrchestratorService(context, _provider, NullLogger<SyncOrchestratorService>.Instance);
        var unassignedPayload = _simulator.GenerateServiceHookPayload(new ExternalWorkItemDto(
            "ADO-ASN-1",
            "https://dev.azure.com/org/p/_workitems/edit/ADO-ASN-1",
            "Task A",
            "Desc",
            "To Do",
            "Task",
            null));

        var result = await orchestrator.ProcessInboundWebhookAsync(ExternalSourceType.AzureDevOps, unassignedPayload, new Dictionary<string, string>());
        result.Status.Should().Be("Applied");

        var reloaded = await context.WorkItems.Include(w => w.Assignee).FirstAsync(w => w.Id == item.Id);
        reloaded.AssigneeId.Should().BeNull("when remote webhook unassigns a work item, local AssigneeId must be cleared so local state matches LastSyncedContentHash");
    }

    [Fact]
    public async Task Sync_InboundWebhook_OversizedTitleAndExternalId_ShouldEnforceSchemaLimits()
    {
        using var context = CreateSqliteContext();
        var project = new Project
        {
            Code = "BIG",
            Name = "Oversized Payload Project",
            ExternalSource = ExternalSourceType.AzureDevOps,
            IsSyncEnabled = true,
            IsSimulatedMode = true
        };
        var emp = new Employee { FullName = "Dev", Email = "dev@big.local", Department = "Eng", JobTitle = "Dev" };
        context.Projects.Add(project);
        context.Employees.Add(emp);
        await context.SaveChangesAsync();

        var longExternalId = new string('9', 40); // "EXT-" + 40 chars = 44 chars > WorkItem.Code MaxLength(30)
        var longTitle = new string('T', 300);     // 300 chars > WorkItem.Title MaxLength(200)
        var orchestrator = new SyncOrchestratorService(context, _provider, NullLogger<SyncOrchestratorService>.Instance);
        var payload = _simulator.GenerateServiceHookPayload(new ExternalWorkItemDto(
            longExternalId,
            "https://dev.azure.com/org/p/_workitems/edit/" + longExternalId,
            longTitle,
            new string('D', 5000),
            "To Do",
            "Task",
            null));

        var result = await orchestrator.ProcessInboundWebhookAsync(ExternalSourceType.AzureDevOps, payload, new Dictionary<string, string>());
        result.Status.Should().Be("Applied");

        var imported = await context.WorkItems.FindAsync(result.WorkItemId!.Value);
        imported!.Code.Length.Should().BeLessThanOrEqualTo(30, "WorkItem.Code is configured with MaxLength(30) for PostgreSQL");
        imported.Title.Length.Should().BeLessThanOrEqualTo(200, "WorkItem.Title is configured with MaxLength(200) for PostgreSQL");
    }

    [Fact]
    public async Task ConflictDeduplication_MergeGuardsAndKeepBothAndSkipIdempotency()
    {
        using var context = CreateSqliteContext();
        var project = new Project
        {
            Code = "DED",
            Name = "Dedup Edge Cases",
            ExternalSource = ExternalSourceType.AzureDevOps,
            IsSyncEnabled = true
        };
        var emp = new Employee { FullName = "Dev", Email = "dev@ded.local", Department = "Eng", JobTitle = "Dev" };
        context.Projects.Add(project);
        context.Employees.Add(emp);

        var alreadyLinkedItem = new WorkItem
        {
            ProjectId = project.Id,
            CreatedById = emp.Id,
            Code = "DED-1",
            Title = "Already Linked",
            ExternalSource = ExternalSourceType.AzureDevOps,
            ExternalId = "ADO-EXISTING-1"
        };
        var unlinkedItem = new WorkItem
        {
            ProjectId = project.Id,
            CreatedById = emp.Id,
            Code = "DED-2",
            Title = "Unlinked Item",
            ExternalId = null
        };
        context.WorkItems.AddRange(alreadyLinkedItem, unlinkedItem);

        var unlinkedConflict = new SyncConflict
        {
            ProjectId = project.Id,
            LocalWorkItemId = null,
            ExternalSource = ExternalSourceType.AzureDevOps,
            ExternalId = "ADO-NEW-1",
            ExternalTitle = "Orphan",
            Status = ConflictStatus.Pending
        };
        var alreadyLinkedConflict = new SyncConflict
        {
            ProjectId = project.Id,
            LocalWorkItemId = alreadyLinkedItem.Id,
            ExternalSource = ExternalSourceType.AzureDevOps,
            ExternalId = "ADO-NEW-2",
            ExternalTitle = "Already Linked",
            Status = ConflictStatus.Pending
        };
        var keepBothConflict = new SyncConflict
        {
            ProjectId = project.Id,
            LocalWorkItemId = unlinkedItem.Id,
            ExternalSource = ExternalSourceType.AzureDevOps,
            ExternalId = "ADO-NEW-3",
            ExternalTitle = "Keep Both Candidate",
            ExternalStatus = "Active",
            ExternalAssigneeEmail = emp.Email,
            Status = ConflictStatus.Pending
        };
        var skipConflict = new SyncConflict
        {
            ProjectId = project.Id,
            LocalWorkItemId = unlinkedItem.Id,
            ExternalSource = ExternalSourceType.AzureDevOps,
            ExternalId = "ADO-NEW-4",
            ExternalTitle = "Skip Candidate",
            ExternalStatus = "New",
            Status = ConflictStatus.Pending
        };
        context.SyncConflicts.AddRange(unlinkedConflict, alreadyLinkedConflict, keepBothConflict, skipConflict);
        await context.SaveChangesAsync();

        var orchestrator = new SyncOrchestratorService(context, _provider, NullLogger<SyncOrchestratorService>.Instance);

        // 1. Merge guard on unlinked conflict
        Func<Task> mergeUnlinked = () => orchestrator.ResolveConflictAsync(unlinkedConflict.Id, ConflictResolutionAction.Merge);
        await mergeUnlinked.Should().ThrowAsync<InvalidOperationException>().WithMessage("*no linked local work item*");
        (await context.SyncConflicts.FindAsync(unlinkedConflict.Id))!.Status.Should().Be(ConflictStatus.Pending);

        // 2. Merge guard when local item is already linked to a different ExternalId
        Func<Task> mergeAlreadyLinked = () => orchestrator.ResolveConflictAsync(alreadyLinkedConflict.Id, ConflictResolutionAction.Merge);
        await mergeAlreadyLinked.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already linked*");
        (await context.SyncConflicts.FindAsync(alreadyLinkedConflict.Id))!.Status.Should().Be(ConflictStatus.Pending);

        // 3. KeepBoth returns newly created WorkItemId (not localItem.Id) and keeps localItem unlinked
        var keepBothRes = await orchestrator.ResolveConflictAsync(keepBothConflict.Id, ConflictResolutionAction.KeepBoth);
        keepBothRes.Resolved.Should().BeTrue();
        keepBothRes.ResolvedWorkItemId.Should().NotBeNull().And.NotBe(unlinkedItem.Id);
        (await context.WorkItems.FindAsync(unlinkedItem.Id))!.ExternalId.Should().BeNull();
        var createdItem = await context.WorkItems.FindAsync(keepBothRes.ResolvedWorkItemId!.Value);
        createdItem.Should().NotBeNull();
        createdItem!.ExternalId.Should().Be("ADO-NEW-3");
        createdItem.Code.Should().Be("ADO-NEW-3");

        // 4. Skip marks resolved with null ResolvedWorkItemId, rejects re-resolution, and skips subsequent webhooks
        var skipRes = await orchestrator.ResolveConflictAsync(skipConflict.Id, ConflictResolutionAction.Skip);
        skipRes.Resolved.Should().BeTrue();
        skipRes.ResolvedWorkItemId.Should().BeNull();

        Func<Task> reResolveSkip = () => orchestrator.ResolveConflictAsync(skipConflict.Id, ConflictResolutionAction.Skip);
        await reResolveSkip.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already resolved*");

        var skipPayload = _simulator.GenerateServiceHookPayload(new ExternalWorkItemDto(
            "ADO-NEW-4", "https://dev.azure.com/org/p/_workitems/edit/ADO-NEW-4", "Skip Candidate", null, "New", "Task", null));
        var webhookAfterSkip = await orchestrator.ProcessInboundWebhookAsync(ExternalSourceType.AzureDevOps, skipPayload, new Dictionary<string, string>());
        webhookAfterSkip.Status.Should().Be("Skipped");
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler;

        public StubHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => _handler(request);
    }
}
