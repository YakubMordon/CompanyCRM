import apiClient from './apiClient';
import {
  CreateOrUpdateWeeklyPlanDto,
  CutoffExecutionSummaryDto,
  DailyPlanDto,
  PlanningCutoffConfigDto,
  SubmitDailyPlanRequestDto,
  UpdatePlanningCutoffConfigDto,
  WeeklyPlanDto,
} from '../types';

export const planningApi = {
  getWeeklyPlans: async (params?: {
    weekStartDate?: string;
    projectId?: string;
    employeeId?: string;
  }): Promise<WeeklyPlanDto[]> => {
    const response = await apiClient.get<WeeklyPlanDto[]>('/planning/weekly', { params });
    return response.data;
  },

  createOrUpdateWeeklyPlan: async (
    dto: CreateOrUpdateWeeklyPlanDto
  ): Promise<WeeklyPlanDto> => {
    const response = await apiClient.post<WeeklyPlanDto>('/planning/weekly', dto);
    return response.data;
  },

  getDailyPlans: async (params?: {
    date?: string;
    employeeId?: string;
  }): Promise<DailyPlanDto[]> => {
    const response = await apiClient.get<DailyPlanDto[]>('/planning/daily', { params });
    return response.data;
  },

  submitDailyPlan: async (dto: SubmitDailyPlanRequestDto): Promise<DailyPlanDto> => {
    const response = await apiClient.post<DailyPlanDto>('/planning/daily', dto);
    return response.data;
  },

  getCutoffConfig: async (projectId?: string): Promise<PlanningCutoffConfigDto> => {
    const response = await apiClient.get<PlanningCutoffConfigDto>('/planning/cutoff-config', {
      params: { projectId },
    });
    return response.data;
  },

  updateCutoffConfig: async (
    dto: UpdatePlanningCutoffConfigDto
  ): Promise<PlanningCutoffConfigDto> => {
    const response = await apiClient.put<PlanningCutoffConfigDto>(
      '/planning/cutoff-config',
      dto
    );
    return response.data;
  },

  runCutoffCheckNow: async (projectId?: string | null): Promise<CutoffExecutionSummaryDto> => {
    const response = await apiClient.post<CutoffExecutionSummaryDto>(
      '/planning/cutoff-check/run-now',
      null,
      projectId ? { params: { projectId } } : undefined
    );
    return response.data;
  },
};
