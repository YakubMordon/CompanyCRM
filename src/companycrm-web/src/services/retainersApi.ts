import { apiClient } from './apiClient';

export type RetainerHealthStatus = 'Healthy' | 'Warning' | 'Exceeded';

export interface RetainerStatusDto {
  id: string;
  projectId: string;
  projectName: string;
  projectCode: string;
  clientName: string;
  monthlyRetainerHours: number;
  monthlyBudgetAmount: number;
  currency: string;
  warningThresholdPercent: number;
  isActive: boolean;
  notifySlackTeams: boolean;
  notifyEmail: boolean;
  year: number;
  month: number;
  loggedBillableHours: number;
  loggedBillableAmount: number;
  remainingHours: number;
  remainingBudget: number;
  hoursUtilizationPercent: number;
  budgetUtilizationPercent: number;
  healthStatus: RetainerHealthStatus;
  updatedAt: string;
}

export interface UpsertRetainerRequestDto {
  projectId: string;
  clientName: string;
  monthlyRetainerHours: number;
  monthlyBudgetAmount: number;
  currency?: string;
  warningThresholdPercent?: number;
  isActive?: boolean;
  notifySlackTeams?: boolean;
  notifyEmail?: boolean;
}

export interface BudgetOverageEvaluationResultDto {
  evaluatedRetainers: number;
  alertsTriggered: number;
  triggeredMessages: string[];
}

export const retainersApi = {
  getRetainers: async (params?: {
    projectId?: string;
    year?: number;
    month?: number;
  }): Promise<RetainerStatusDto[]> => {
    const response = await apiClient.get<RetainerStatusDto[]>('/retainers', { params });
    return response.data;
  },

  upsertRetainer: async (dto: UpsertRetainerRequestDto): Promise<RetainerStatusDto> => {
    const response = await apiClient.put<RetainerStatusDto>('/retainers', dto);
    return response.data;
  },

  deleteRetainer: async (id: string): Promise<void> => {
    await apiClient.delete(`/retainers/${id}`);
  },

  evaluateAlerts: async (params?: {
    year?: number;
    month?: number;
  }): Promise<BudgetOverageEvaluationResultDto> => {
    const response = await apiClient.post<BudgetOverageEvaluationResultDto>('/retainers/evaluate-alerts', null, {
      params,
    });
    return response.data;
  },
};
