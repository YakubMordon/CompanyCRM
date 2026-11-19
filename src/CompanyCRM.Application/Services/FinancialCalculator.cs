namespace CompanyCRM.Application.Services;

public static class FinancialCalculator
{
    /// <summary>
    /// Calculates total billable client revenue: Sum(DurationHours * HourlyBillableRate) for all billable time entries.
    /// </summary>
    public static decimal CalculateProjectRevenue(IEnumerable<(decimal DurationMinutes, decimal HourlyBillableRate, bool IsBillable)> entries)
    {
        var sum = entries
            .Where(e => e.IsBillable && e.DurationMinutes > 0.0m && e.HourlyBillableRate > 0.0m)
            .Sum(e => (e.DurationMinutes * e.HourlyBillableRate) / 60.0m);
        return Math.Round(sum, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Calculates total internal labor cost: Sum(DurationHours * HourlyCostRate) for ALL time entries (billable and non-billable).
    /// </summary>
    public static decimal CalculateProjectInternalCost(IEnumerable<(decimal DurationMinutes, decimal HourlyCostRate)> entries)
    {
        var sum = entries
            .Where(e => e.DurationMinutes > 0.0m && e.HourlyCostRate > 0.0m)
            .Sum(e => (e.DurationMinutes * e.HourlyCostRate) / 60.0m);
        return Math.Round(sum, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Calculates gross profit in dollars and profit margin percentage.
    /// Margin % = (GrossProfit / Revenue) * 100. Safely returns 0.00% if Revenue is 0.
    /// </summary>
    public static (decimal GrossProfit, decimal ProfitMarginPercent) CalculateProfitability(decimal revenue, decimal internalCost)
    {
        var grossProfit = revenue - internalCost;
        var marginPercent = revenue > 0.0m
            ? Math.Round((grossProfit / revenue) * 100.0m, 2, MidpointRounding.AwayFromZero)
            : 0.0m;
        return (grossProfit, marginPercent);
    }

    /// <summary>
    /// Calculates employee utilization percentage: (BillableHours / WeeklyCapacityHours) * 100.
    /// Safely returns 0.00% if capacity is zero or billable hours are non-positive.
    /// </summary>
    public static decimal CalculateUtilizationPercent(decimal billableHours, decimal totalCapacityHours)
    {
        if (totalCapacityHours <= 0.0m || billableHours <= 0.0m) return 0.0m;
        return Math.Round((billableHours / totalCapacityHours) * 100.0m, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Calculates daily plan compliance rate % and on-time submission rate %.
    /// </summary>
    public static (decimal CompliancePercent, decimal OnTimePercent) CalculatePlanCompliance(int workingDays, int submittedCount, int onTimeCount)
    {
        if (workingDays <= 0) return (0.0m, 0.0m);
        var safeSubmitted = Math.Max(0, submittedCount);
        var safeOnTime = Math.Max(0, onTimeCount);
        var compliance = Math.Round(((decimal)safeSubmitted / workingDays) * 100.0m, 2, MidpointRounding.AwayFromZero);
        var onTime = Math.Round(((decimal)safeOnTime / workingDays) * 100.0m, 2, MidpointRounding.AwayFromZero);
        return (compliance, onTime);
    }
}
