import {
  CompletionRateDto,
  EmployeeProjectProfitabilityDto,
} from '../../types';

export function computeEmployeeMargin(
  emp: Pick<
    EmployeeProjectProfitabilityDto,
    'revenue' | 'billedRevenue' | 'grossProfit' | 'grossMarginPercent'
  >
): { revenue: number; marginPercent: number; isProfitable: boolean } {
  const revenue = emp.revenue ?? emp.billedRevenue ?? 0;
  const marginPercent =
    emp.grossMarginPercent !== undefined
      ? emp.grossMarginPercent
      : revenue > 0
        ? (emp.grossProfit / revenue) * 100
        : 0;
  return {
    revenue,
    marginPercent,
    isProfitable: emp.grossProfit >= 0,
  };
}

export function buildCompletionChartData(
  data: Pick<
    CompletionRateDto,
    | 'statusCounts'
    | 'backlogItems'
    | 'todoItems'
    | 'inProgressItems'
    | 'inVerifyItems'
    | 'doneItems'
  >
): { name: string; value: number }[] {
  const counts = data.statusCounts || {};
  return [
    { name: 'Backlog', value: counts['Backlog'] ?? data.backlogItems ?? 0 },
    { name: 'Todo', value: counts['Todo'] ?? data.todoItems ?? 0 },
    { name: 'In Progress', value: counts['InProgress'] ?? data.inProgressItems ?? 0 },
    { name: 'In Verify', value: counts['InVerify'] ?? data.inVerifyItems ?? 0 },
    { name: 'Done', value: counts['Done'] ?? data.doneItems ?? 0 },
  ].filter((d) => d.value > 0);
}
