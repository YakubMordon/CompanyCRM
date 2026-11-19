namespace CompanyCRM.Domain.ValueObjects;

public record FinancialMetrics(
    decimal TotalBillableHours,
    decimal TotalNonBillableHours,
    decimal TotalHours,
    decimal BillableRevenue,
    decimal InternalCost,
    decimal GrossProfit,
    decimal ProfitMarginPercent);
