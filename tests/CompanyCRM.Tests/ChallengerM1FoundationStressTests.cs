namespace CompanyCRM.Tests;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Xunit;
using FluentAssertions;
using CompanyCRM.Application.Services;
using CompanyCRM.Domain.Enums;

public class ChallengerM1FoundationStressTests
{
    // =========================================================================
    // SECTION 1: SyncContentHasher Adversarial Stress Tests
    // =========================================================================

    [Fact]
    public void Hasher_DeterminismStress_1000Iterations_ProduceIdenticalHash()
    {
        const string title = "Refactor Authentication Pipeline";
        const string desc = "Migrate from legacy session tokens to JWT bearer tokens.\nValidate refresh token rotation.";
        const WorkItemStatus status = WorkItemStatus.InProgress;
        const WorkItemType type = WorkItemType.Story;
        const string email = "lead.dev@companycrm.internal";

        var baseline = SyncContentHasher.ComputeHash(title, desc, status, type, email);

        for (int i = 0; i < 1000; i++)
        {
            var current = SyncContentHasher.ComputeHash(title, desc, status, type, email);
            current.Should().Be(baseline, "Hash must be completely deterministic across all iterations");
        }
    }

    [Theory]
    [InlineData("Line 1\r\nLine 2\r\nLine 3", "Line 1\nLine 2\nLine 3")]
    [InlineData("Line 1\r\n\r\nLine 2", "Line 1\n\nLine 2")]
    [InlineData("\r\nSingle Line\r\n", "Single Line")]
    [InlineData("Trailing CRLF\r\n", "Trailing CRLF\n")]
    public void Hasher_CrlfVsLfNormalization_ProducesIdenticalHash(string crlfVersion, string lfVersion)
    {
        var hashCrlf = SyncContentHasher.ComputeHash("Task Title", crlfVersion, WorkItemStatus.Todo, WorkItemType.Task, "dev@crm.com");
        var hashLf = SyncContentHasher.ComputeHash("Task Title", lfVersion, WorkItemStatus.Todo, WorkItemType.Task, "dev@crm.com");

        hashCrlf.Should().Be(hashLf, "CRLF and LF differences must normalize to identical canonical representation");
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("\t\t  ", "")]
    [InlineData("\r\n", "")]
    [InlineData(null, "")]
    [InlineData("", null)]
    public void Hasher_NullVsEmptyVsWhitespaceDescription_NormalizesEquivalently(string? desc1, string? desc2)
    {
        var hash1 = SyncContentHasher.ComputeHash("Title", desc1, WorkItemStatus.Todo, WorkItemType.Task, "dev@crm.com");
        var hash2 = SyncContentHasher.ComputeHash("Title", desc2, WorkItemStatus.Todo, WorkItemType.Task, "dev@crm.com");

        hash1.Should().Be(hash2, "Null, empty, and whitespace-only descriptions must normalize equivalently");
    }

    [Theory]
    [InlineData("john.doe@company.com", "JOHN.DOE@COMPANY.COM")]
    [InlineData("John.Doe@Company.Com", "john.doe@company.com")]
    [InlineData("  john.doe@company.com  ", "john.doe@company.com")]
    [InlineData("\tJohn.Doe@Company.Com\r\n", "john.doe@company.com")]
    [InlineData(null, "")]
    [InlineData(null, "   ")]
    public void Hasher_EmailCasingAndWhitespace_NormalizesEquivalently(string? email1, string? email2)
    {
        var hash1 = SyncContentHasher.ComputeHash("Title", "Desc", WorkItemStatus.Todo, WorkItemType.Task, email1);
        var hash2 = SyncContentHasher.ComputeHash("Title", "Desc", WorkItemStatus.Todo, WorkItemType.Task, email2);

        hash1.Should().Be(hash2, "Email casing and leading/trailing whitespace must normalize identically");
    }

    [Fact]
    public void Hasher_SingleFieldMutation_AlwaysProducesDifferentHash()
    {
        const string baseTitle = "Title";
        const string baseDesc = "Description";
        const WorkItemStatus baseStatus = WorkItemStatus.Todo;
        const WorkItemType baseType = WorkItemType.Task;
        const string baseEmail = "dev@crm.com";

        var baseHash = SyncContentHasher.ComputeHash(baseTitle, baseDesc, baseStatus, baseType, baseEmail);

        // 1. Mutate Title
        SyncContentHasher.ComputeHash("Title 2", baseDesc, baseStatus, baseType, baseEmail)
            .Should().NotBe(baseHash);

        // 2. Mutate Description
        SyncContentHasher.ComputeHash(baseTitle, "Description Updated", baseStatus, baseType, baseEmail)
            .Should().NotBe(baseHash);

        // 3. Mutate Status
        SyncContentHasher.ComputeHash(baseTitle, baseDesc, WorkItemStatus.InProgress, baseType, baseEmail)
            .Should().NotBe(baseHash);

        // 4. Mutate Type
        SyncContentHasher.ComputeHash(baseTitle, baseDesc, baseStatus, WorkItemType.Bug, baseEmail)
            .Should().NotBe(baseHash);

        // 5. Mutate Email
        SyncContentHasher.ComputeHash(baseTitle, baseDesc, baseStatus, baseType, "other@crm.com")
            .Should().NotBe(baseHash);
    }

    [Theory]
    [InlineData("🚀 Implement AI Copilot Feature", "Emoji title with UTF-8 symbols: 🔥 💻 ⚡")]
    [InlineData("ユーザー管理システムの改修", "日本語のマルチバイト文字の説明文テスト。")]
    [InlineData("Überwachung des Systems", "Prüfung von Groß- und Kleinschreibung sowie Umlauten: ä, ö, ü, ß.")]
    [InlineData("إصلاح خطأ في النظام", "وصف تفصيلي للمهمة باللغة العربية مع دعم RTL.")]
    public void Hasher_UnicodeAndInternationalStrings_GeneratesValidSha256(string title, string desc)
    {
        var hash = SyncContentHasher.ComputeHash(title, desc, WorkItemStatus.InVerify, WorkItemType.Story, "i18n@crm.global");

        hash.Should().NotBeNullOrWhiteSpace();
        hash.Should().HaveLength(64);
        hash.Should().MatchRegex("^[A-F0-9]{64}$");
    }

    [Fact]
    public void Hasher_LargePayloadStress_SurvivesWithoutExceptionOrLag()
    {
        // 10,000 char title, 100,000 char description
        var largeTitle = new string('T', 10_000);
        var largeDesc = string.Join("\n", Enumerable.Repeat("This is a detailed line of description for a work item in CompanyCRM.", 1_500));

        var sw = Stopwatch.StartNew();
        var hash = SyncContentHasher.ComputeHash(largeTitle, largeDesc, WorkItemStatus.InProgress, WorkItemType.Task, "perf@crm.com");
        sw.Stop();

        hash.Should().HaveLength(64);
        sw.ElapsedMilliseconds.Should().BeLessThan(200, "Hashing 100KB+ strings must take < 200ms");
    }

    [Fact]
    public void Hasher_DelimiterCollisionVulnerability_DemonstratesRisk()
    {
        // Demonstrating canonical pipe escaping prevents delimiter collision:
        // Item A: Title="Item|Part2", Desc="Rest"
        // Item B: Title="Item", Desc="Part2|Rest"
        var hashA = SyncContentHasher.ComputeHash("Item|Part2", "Rest", WorkItemStatus.Todo, WorkItemType.Task, "user@crm.com");
        var hashB = SyncContentHasher.ComputeHash("Item", "Part2|Rest", WorkItemStatus.Todo, WorkItemType.Task, "user@crm.com");

        // Escaped pipe characters ensure both produce distinct canonical hashes
        hashA.Should().NotBe(hashB, "Escaped pipe delimiters prevent collisions across adjacent string boundaries");
    }

    // =========================================================================
    // SECTION 2: FinancialCalculator Adversarial Stress Tests
    // =========================================================================

    [Fact]
    public void Financial_ZeroRevenue_PositiveCost_ReturnsNegativeProfit_ZeroMargin_NoException()
    {
        // Revenue = $0.00, Cost = $1,500.00
        var (profit, margin) = FinancialCalculator.CalculateProfitability(0.00m, 1500.00m);

        profit.Should().Be(-1500.00m);
        margin.Should().Be(0.00m, "Margin must safely return 0.00% without throwing DivideByZeroException when revenue is 0");
    }

    [Fact]
    public void Financial_ZeroRevenue_ZeroCost_ReturnsZeroProfitAndMargin()
    {
        var (profit, margin) = FinancialCalculator.CalculateProfitability(0.00m, 0.00m);

        profit.Should().Be(0.00m);
        margin.Should().Be(0.00m);
    }

    [Fact]
    public void Financial_NegativeRevenue_CreditOrAdjustment_DoesNotThrowDivideByZero()
    {
        // Revenue = -$500.00 (e.g. credit/refund adjustment), Cost = $200.00
        var (profit, margin) = FinancialCalculator.CalculateProfitability(-500.00m, 200.00m);

        profit.Should().Be(-700.00m);
        margin.Should().Be(0.00m, "Margin guard 'revenue > 0' prevents dividing by negative or zero revenue");
    }

    [Fact]
    public void Financial_ExtremeCostExceedingRevenue_ProducesNegativeMargin()
    {
        // Revenue = $1,000.00, Cost = $3,000.00 -> Profit = -$2,000.00 -> Margin = -200.00%
        var (profit, margin) = FinancialCalculator.CalculateProfitability(1000.00m, 3000.00m);

        profit.Should().Be(-2000.00m);
        margin.Should().Be(-200.00m);
    }

    [Fact]
    public void Financial_CalculateProjectRevenue_OnlyCountsBillableEntries()
    {
        var entries = new List<(decimal DurationMinutes, decimal HourlyBillableRate, bool IsBillable)>
        {
            (60m, 200.00m, true),   // 1.0 hr * $200 = $200.00
            (120m, 150.00m, false), // 2.0 hrs non-billable = $0.00
            (30m, 100.00m, true),   // 0.5 hr * $100 = $50.00
            (0m, 250.00m, true),    // 0 hrs = $0.00
        };

        var revenue = FinancialCalculator.CalculateProjectRevenue(entries);
        revenue.Should().Be(250.00m);
    }

    [Fact]
    public void Financial_CalculateProjectInternalCost_CountsBothBillableAndNonBillableEntries()
    {
        var entries = new List<(decimal DurationMinutes, decimal HourlyCostRate)>
        {
            (60m, 60.00m),   // 1.0 hr * $60 = $60.00
            (120m, 50.00m),  // 2.0 hrs * $50 = $100.00
            (30m, 80.00m),   // 0.5 hr * $80 = $40.00
            (0m, 100.00m),   // 0 hrs = $0.00
        };

        var cost = FinancialCalculator.CalculateProjectInternalCost(entries);
        cost.Should().Be(200.00m);
    }

    [Theory]
    [InlineData(0.0, 0.0, 0.0)]
    [InlineData(20.0, 0.0, 0.0)]
    [InlineData(40.0, -10.0, 0.0)]
    [InlineData(-5.0, 40.0, 0.0)]
    [InlineData(40.0, 40.0, 100.0)]
    [InlineData(50.0, 40.0, 125.0)]
    [InlineData(10.0, 30.0, 33.33)]
    public void Financial_CalculateUtilizationPercent_HandlesEdgeCasesAndZeroCapacity(
        double billable, 
        double capacity, 
        double expectedPercent)
    {
        var result = FinancialCalculator.CalculateUtilizationPercent((decimal)billable, (decimal)capacity);
        result.Should().Be((decimal)expectedPercent);
    }

    [Theory]
    [InlineData(0, 5, 5, 0.0, 0.0)]     // 0 working days: guard returns 0.0m
    [InlineData(-1, 5, 5, 0.0, 0.0)]    // Negative working days: guard returns 0.0m
    [InlineData(5, 5, 5, 100.0, 100.0)] // Full compliance
    [InlineData(5, 4, 3, 80.0, 60.0)]   // Partial compliance
    [InlineData(7, 5, 4, 71.43, 57.14)] // Decimal precision rounding
    [InlineData(5, 0, 0, 0.0, 0.0)]     // Zero submissions
    [InlineData(5, 6, 6, 120.0, 120.0)] // Over-submission (weekend/extra)
    public void Financial_CalculatePlanCompliance_HandlesZeroDaysAndRounding(
        int workingDays, 
        int submitted, 
        int onTime, 
        double expectedCompliance, 
        double expectedOnTime)
    {
        var (compliance, onTimeRate) = FinancialCalculator.CalculatePlanCompliance(workingDays, submitted, onTime);

        compliance.Should().Be((decimal)expectedCompliance);
        onTimeRate.Should().Be((decimal)expectedOnTime);
    }

    [Fact]
    public void Financial_LargeVolumePerformanceStress_Aggregates100KEntriesInUnder100Ms()
    {
        var entries = Enumerable.Range(1, 100_000)
            .Select(i => (DurationMinutes: (decimal)(i % 120 + 1), HourlyBillableRate: (decimal)(50 + (i % 150)), IsBillable: i % 3 != 0))
            .ToList();

        var sw = Stopwatch.StartNew();
        var revenue = FinancialCalculator.CalculateProjectRevenue(entries);
        var cost = FinancialCalculator.CalculateProjectInternalCost(entries.Select(e => (e.DurationMinutes, e.HourlyBillableRate * 0.4m)));
        var (profit, margin) = FinancialCalculator.CalculateProfitability(revenue, cost);
        sw.Stop();

        revenue.Should().BeGreaterThan(0m);
        cost.Should().BeGreaterThan(0m);
        profit.Should().BeGreaterThan(0m);
        margin.Should().BeGreaterThan(0m);
        sw.ElapsedMilliseconds.Should().BeLessThan(100, "Processing 100,000 entries must take < 100ms");
    }
}
