namespace CompanyCRM.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using FluentAssertions;
using CompanyCRM.Application.Services;
using CompanyCRM.Domain.Entities;
using CompanyCRM.Domain.Enums;
using CompanyCRM.Tests.Common;

public class FinancialCalculationTests
{
    // =========================================================================
    // Tier 1: Feature Coverage (5 test cases)
    // =========================================================================

    [Fact]
    public void CalculateProjectRevenue_WithBillableTimeEntries_ReturnsAccurateSum()
    {
        // Arrange
        // emp1: $150/hr billable
        // emp2: $200/hr billable
        var entries = new List<(decimal DurationMinutes, decimal HourlyBillableRate, bool IsBillable)>
        {
            // 2.0 hrs @ $150/hr = $300.00
            (120m, 150.00m, true),
            // 1.0 hr @ $150/hr non-billable = $0.00
            (60m, 150.00m, false),
            // 3.0 hrs @ $200/hr = $600.00
            (180m, 200.00m, true)
        };

        // Act
        var totalRevenue = FinancialCalculator.CalculateProjectRevenue(entries);

        // Assert
        totalRevenue.Should().Be(900.00m);
    }

    [Fact]
    public void CalculateProjectInternalCost_WithBillableAndNonBillableEntries_ReturnsTotalLaborCost()
    {
        // Arrange
        // emp1: $50/hr cost
        // emp2: $75/hr cost
        var entries = new List<(decimal DurationMinutes, decimal HourlyCostRate)>
        {
            // 2.0 hrs @ $50/hr = $100.00
            (120m, 50.00m),
            // 1.0 hr @ $50/hr = $50.00 (non-billable time still costs internal labor)
            (60m, 50.00m),
            // 2.0 hrs @ $75/hr = $150.00
            (120m, 75.00m)
        };

        // Act
        var totalCost = FinancialCalculator.CalculateProjectInternalCost(entries);

        // Assert
        totalCost.Should().Be(300.00m);
    }

    [Fact]
    public void CalculateGrossProfit_WithRevenueAndCost_ReturnsAccurateDollarDifference()
    {
        // Arrange
        var revenue = 15000.00m;
        var internalCost = 6000.00m;
        var expectedProfit = 9000.00m;

        // Act
        var (grossProfit, _) = FinancialCalculator.CalculateProfitability(revenue, internalCost);

        // Assert
        grossProfit.Should().Be(expectedProfit);
    }

    [Fact]
    public void CalculateGrossMarginPercentage_StandardRevenueAndCost_ReturnsAccuratePercentage()
    {
        // Arrange
        var revenue = 20000.00m;
        var internalCost = 8000.00m; // Gross profit = $12,000.00
        var expectedMargin = 60.00m;  // (12000 / 20000) * 100 = 60.00%

        // Act
        var (_, marginPercent) = FinancialCalculator.CalculateProfitability(revenue, internalCost);

        // Assert
        marginPercent.Should().Be(expectedMargin);
    }

    [Fact]
    public void CalculateEmployeeUtilizationPercentage_StandardCapacity_ReturnsAccurateRate()
    {
        // Arrange
        var billableHours = 30.0m;
        var totalCapacityHours = 40.0m;
        var expectedUtilization = 75.00m; // (30 / 40) * 100 = 75.00%

        // Act
        var utilization = FinancialCalculator.CalculateUtilizationPercent(billableHours, totalCapacityHours);

        // Assert
        utilization.Should().Be(expectedUtilization);
    }

    // =========================================================================
    // Tier 2: Boundary & Corner Cases (5 test cases)
    // =========================================================================

    [Fact]
    public void CalculateProfitability_ZeroBillableRevenueWithPositiveCost_ReturnsZeroMarginWithoutException()
    {
        // Arrange
        var revenue = 0.00m;
        var internalCost = 2500.00m;

        // Act
        Action act = () => FinancialCalculator.CalculateProfitability(revenue, internalCost);

        // Assert
        act.Should().NotThrow<DivideByZeroException>();
        var (profit, margin) = FinancialCalculator.CalculateProfitability(revenue, internalCost);
        profit.Should().Be(-2500.00m);
        margin.Should().Be(0.00m);
    }

    [Fact]
    public void CalculateProjectFinancials_ZeroTimeEntries_ReturnsZeroRevenueCostProfitAndMargin()
    {
        // Arrange
        var emptyRevenueEntries = new List<(decimal DurationMinutes, decimal HourlyBillableRate, bool IsBillable)>();
        var emptyCostEntries = new List<(decimal DurationMinutes, decimal HourlyCostRate)>();

        // Act
        var revenue = FinancialCalculator.CalculateProjectRevenue(emptyRevenueEntries);
        var cost = FinancialCalculator.CalculateProjectInternalCost(emptyCostEntries);
        var (profit, margin) = FinancialCalculator.CalculateProfitability(revenue, cost);

        // Assert
        revenue.Should().Be(0.00m);
        cost.Should().Be(0.00m);
        profit.Should().Be(0.00m);
        margin.Should().Be(0.00m);
    }

    [Fact]
    public void CalculateEmployeeUtilization_ZeroCapacityHours_ReturnsZeroWithoutDivisionByZeroException()
    {
        // Arrange
        var billableHours = 15.0m;
        var zeroCapacity = 0.0m;

        // Act
        Action act = () => FinancialCalculator.CalculateUtilizationPercent(billableHours, zeroCapacity);

        // Assert
        act.Should().NotThrow<DivideByZeroException>();
        var utilization = FinancialCalculator.CalculateUtilizationPercent(billableHours, zeroCapacity);
        utilization.Should().Be(0.00m);
    }

    [Fact]
    public void CalculateEmployeeUtilization_BillableHoursEqualCapacity_ReturnsExactOneHundredPercent()
    {
        // Arrange
        var capacity = 40.0m;
        var billable = 40.0m;

        // Act
        var utilization = FinancialCalculator.CalculateUtilizationPercent(billable, capacity);

        // Assert
        utilization.Should().Be(100.00m);
    }

    [Fact]
    public void CalculateEmployeeUtilization_PartTimeCapacity_ScalesCorrectly()
    {
        // Arrange
        var partTimeCapacity = 20.0m;
        var billableStandard = 15.0m;
        var billableOvertime = 25.0m;

        // Act
        var standardUtil = FinancialCalculator.CalculateUtilizationPercent(billableStandard, partTimeCapacity);
        var overtimeUtil = FinancialCalculator.CalculateUtilizationPercent(billableOvertime, partTimeCapacity);

        // Assert
        standardUtil.Should().Be(75.00m);   // (15 / 20) * 100 = 75.00%
        overtimeUtil.Should().Be(125.00m); // (25 / 20) * 100 = 125.00%
    }

    [Fact]
    public void CalculateProjectRevenue_MultipleFractionalEntries_PreventsPennyDrift()
    {
        // 3 entries of 20 minutes at $100/hr = 60 minutes = 1 hour = $100.00
        // Per-entry rounding would produce 3 * $33.33 = $99.99 (penny drift)
        // Aggregate sum rounding produces exact $100.00
        var entries = new List<(decimal DurationMinutes, decimal HourlyBillableRate, bool IsBillable)>
        {
            (20.0m, 100.00m, true),
            (20.0m, 100.00m, true),
            (20.0m, 100.00m, true)
        };

        var revenue = FinancialCalculator.CalculateProjectRevenue(entries);
        revenue.Should().Be(100.00m);

        var costEntries = new List<(decimal DurationMinutes, decimal HourlyCostRate)>
        {
            (20.0m, 100.00m),
            (20.0m, 100.00m),
            (20.0m, 100.00m)
        };

        var cost = FinancialCalculator.CalculateProjectInternalCost(costEntries);
        cost.Should().Be(100.00m);
    }
}
