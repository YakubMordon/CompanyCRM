namespace CompanyCRM.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using FluentAssertions;
using CompanyCRM.Application.Services;

/// <summary>
/// Adversarial stress test harness verifying FinancialCalculator decimal precision,
/// penny drift immunity, large scale (100,000 entries) processing, and exact decimal arithmetic.
/// </summary>
public class ChallengerM2FinancialPrecisionStressTests
{
    [Fact]
    public void Challenge_Three20MinEntries_At100DollarsPerHour_SumsToExact100DollarsWithoutPennyDrift()
    {
        // 3 entries of 20 min @ $100/hr = 60 mins @ $100/hr = $100.00
        var entries = new List<(decimal DurationMinutes, decimal HourlyBillableRate, bool IsBillable)>
        {
            (20.0m, 100.00m, true),
            (20.0m, 100.00m, true),
            (20.0m, 100.00m, true)
        };

        var revenue = FinancialCalculator.CalculateProjectRevenue(entries);
        revenue.Should().Be(100.00m);
        decimal.Remainder(revenue, 0.01m).Should().Be(0.00m);

        var costEntries = new List<(decimal DurationMinutes, decimal HourlyCostRate)>
        {
            (20.0m, 100.00m),
            (20.0m, 100.00m),
            (20.0m, 100.00m)
        };

        var cost = FinancialCalculator.CalculateProjectInternalCost(costEntries);
        cost.Should().Be(100.00m);
        decimal.Remainder(cost, 0.01m).Should().Be(0.00m);
    }

    [Fact]
    public void Challenge_Six10MinEntries_At100DollarsPerHour_SumsToExact100DollarsWithoutPennyDrift()
    {
        // 6 entries of 10 min @ $100/hr = 60 mins @ $100/hr = $100.00
        var entries = Enumerable.Range(0, 6)
            .Select(_ => (10.0m, 100.00m, true))
            .ToList();

        var revenue = FinancialCalculator.CalculateProjectRevenue(entries);
        revenue.Should().Be(100.00m);
        decimal.Remainder(revenue, 0.01m).Should().Be(0.00m);
    }

    [Fact]
    public void Challenge_Sixty1MinEntries_At100DollarsPerHour_SumsToExact100DollarsWithoutPennyDrift()
    {
        // 60 entries of 1 min @ $100/hr = 60 mins @ $100/hr = $100.00
        var entries = Enumerable.Range(0, 60)
            .Select(_ => (1.0m, 100.00m, true))
            .ToList();

        var revenue = FinancialCalculator.CalculateProjectRevenue(entries);
        revenue.Should().Be(100.00m);
        decimal.Remainder(revenue, 0.01m).Should().Be(0.00m);
    }

    [Fact]
    public void Challenge_OneHundredThousandEntries_UniformCycles_SumsToExactDollars()
    {
        // 100,000 entries repeating 5-part cycle: (1 min, 7 min, 13 min, 17 min, 22 min = 60 mins = 1 hr)
        // 20,000 cycles * 1 hr = 20,000 hours @ $125.00/hr = $2,500,000.00 exact
        var pattern = new[] { 1.0m, 7.0m, 13.0m, 17.0m, 22.0m };
        var entries = new List<(decimal DurationMinutes, decimal HourlyBillableRate, bool IsBillable)>(100_000);

        for (int i = 0; i < 100_000; i++)
        {
            entries.Add((pattern[i % pattern.Length], 125.00m, true));
        }

        var revenue = FinancialCalculator.CalculateProjectRevenue(entries);
        revenue.Should().Be(2_500_000.00m);
        decimal.Remainder(revenue, 0.01m).Should().Be(0.00m);

        // Also verify cost calculation
        var costEntries = entries.Select(e => (e.DurationMinutes, e.HourlyBillableRate)).ToList();
        var cost = FinancialCalculator.CalculateProjectInternalCost(costEntries);
        cost.Should().Be(2_500_000.00m);
        decimal.Remainder(cost, 0.01m).Should().Be(0.00m);
    }

    [Fact]
    public void Challenge_OneHundredThousandEntries_UniformCycles_AtRate3785_SumsToExactDollars()
    {
        // 100,000 entries repeating 5-part cycle: (1 min, 7 min, 13 min, 17 min, 22 min = 60 mins = 1 hr)
        // 20,000 cycles * 1 hr = 20,000 hours @ $37.85/hr = $757,000.00 exact
        var pattern = new[] { 1.0m, 7.0m, 13.0m, 17.0m, 22.0m };
        var entries = new List<(decimal DurationMinutes, decimal HourlyBillableRate, bool IsBillable)>(100_000);

        for (int i = 0; i < 100_000; i++)
        {
            entries.Add((pattern[i % pattern.Length], 37.85m, true));
        }

        var revenue = FinancialCalculator.CalculateProjectRevenue(entries);
        revenue.Should().Be(757_000.00m);
        decimal.Remainder(revenue, 0.01m).Should().Be(0.00m);
    }

    [Fact]
    public void Challenge_OneHundredThousandEntries_FractionalDurationsAndVariousRates_PreservesExactPrecision()
    {
        // 100,000 entries with mixed fractional durations (1 min, 7 min, 13 min, 17 min, 22 min)
        // and mixed rates ($37.85, $125.00, $0.00):
        // Cycle of 5:
        // i % 5 == 0: 1 min @ $37.85
        // i % 5 == 1: 7 min @ $125.00
        // i % 5 == 2: 13 min @ $37.85
        // i % 5 == 3: 17 min @ $125.00
        // i % 5 == 4: 22 min @ $0.00
        //
        // Over 20,000 cycles:
        // Duration at $37.85: 20,000 * (1 + 13) = 280,000 min => (280,000 * 37.85) / 60 = 176,633.3333333333333333333333m
        // Duration at $125.00: 20,000 * (7 + 17) = 480,000 min = 8,000 hr => 8,000 * 125 = 1,000,000.00m
        // Duration at $0.00: 20,000 * 22 = 440,000 min => 0.00m
        // Exact sum = 1,176,633.3333333333333333333333m
        // Rounded to 2 decimals = 1,176,633.33m

        var durations = new[] { 1.0m, 7.0m, 13.0m, 17.0m, 22.0m };
        var rates = new[] { 37.85m, 125.00m, 37.85m, 125.00m, 0.00m };

        var entries = new List<(decimal DurationMinutes, decimal HourlyBillableRate, bool IsBillable)>(100_000);
        for (int i = 0; i < 100_000; i++)
        {
            int idx = i % 5;
            entries.Add((durations[idx], rates[idx], true));
        }

        var revenue = FinancialCalculator.CalculateProjectRevenue(entries);
        revenue.Should().Be(1_176_633.33m);
        decimal.Remainder(revenue, 0.01m).Should().Be(0.00m);
    }

    [Fact]
    public void Challenge_OneHundredThousandEntries_ZeroRate_SumsToExactZero()
    {
        var entries = Enumerable.Range(0, 100_000)
            .Select(i => ((decimal)(i % 60 + 1), 0.00m, true))
            .ToList();

        var revenue = FinancialCalculator.CalculateProjectRevenue(entries);
        revenue.Should().Be(0.00m);

        var costEntries = entries.Select(e => (e.Item1, e.Item2)).ToList();
        var cost = FinancialCalculator.CalculateProjectInternalCost(costEntries);
        cost.Should().Be(0.00m);
    }

    [Fact]
    public void Challenge_OneHundredThousandEntries_MixedBillableAndCost_VerifiesGrossProfitAndMargin()
    {
        // 100,000 entries of 12 minutes (0.2 hours) each:
        // 50,000 billable @ $125.00/hr billable rate
        // 50,000 non-billable @ $0.00/hr billable rate
        // Cost rate for ALL 100,000 entries = $50.00/hr
        //
        // Billable hours = 50,000 * 0.2 = 10,000 hrs => Revenue = 10,000 * $125 = $1,250,000.00
        // Total hours = 100,000 * 0.2 = 20,000 hrs => Internal Cost = 20,000 * $50 = $1,000,000.00
        // Gross Profit = $1,250,000.00 - $1,000,000.00 = $250,000.00
        // Profit Margin = (250,000 / 1,250,000) * 100 = 20.00%

        var revenueEntries = new List<(decimal DurationMinutes, decimal HourlyBillableRate, bool IsBillable)>(100_000);
        var costEntries = new List<(decimal DurationMinutes, decimal HourlyCostRate)>(100_000);

        for (int i = 0; i < 100_000; i++)
        {
            bool isBillable = (i % 2 == 0);
            decimal billableRate = isBillable ? 125.00m : 0.00m;
            revenueEntries.Add((12.0m, billableRate, isBillable));
            costEntries.Add((12.0m, 50.00m));
        }

        var revenue = FinancialCalculator.CalculateProjectRevenue(revenueEntries);
        var cost = FinancialCalculator.CalculateProjectInternalCost(costEntries);
        var (profit, margin) = FinancialCalculator.CalculateProfitability(revenue, cost);

        revenue.Should().Be(1_250_000.00m);
        cost.Should().Be(1_000_000.00m);
        profit.Should().Be(250_000.00m);
        margin.Should().Be(20.00m);
    }

    [Theory]
    [InlineData(1, 37.85)]
    [InlineData(7, 37.85)]
    [InlineData(13, 125.00)]
    [InlineData(17, 125.00)]
    [InlineData(59, 999.99)]
    public void Challenge_NoFloatingPointRemainder_ScalePreserved(int minutes, double rateDouble)
    {
        decimal rate = (decimal)rateDouble;
        var entries = new[] { ((decimal)minutes, rate, true) };

        var revenue = FinancialCalculator.CalculateProjectRevenue(entries);

        // Verify decimal has at most 2 decimal places and no fractional penny
        (revenue * 100m).Should().Be(Math.Floor(revenue * 100m));
        decimal.Remainder(revenue, 0.01m).Should().Be(0.00m);
    }
}
