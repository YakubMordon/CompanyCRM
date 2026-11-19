import apiClient from './apiClient';
import {
  BurndownDto,
  CompletionRateDto,
  EmployeeCompensationDto,
  HoursComparisonDto,
  ProjectProfitabilityDto,
  SprintVelocityDto,
} from '../types';

export const managementApi = {
  getCompensation: async (): Promise<EmployeeCompensationDto[]> => {
    const response = await apiClient.get<EmployeeCompensationDto[]>('/management/compensation');
    return response.data;
  },

  getProfitability: async (
    projectId: string,
    startDate?: string,
    endDate?: string
  ): Promise<ProjectProfitabilityDto> => {
    const response = await apiClient.get<ProjectProfitabilityDto>(
      '/management/profitability',
      {
        params: { projectId, startDate, endDate },
      }
    );
    return response.data;
  },

  getVelocity: async (
    projectId: string,
    sprintsCount = 5
  ): Promise<SprintVelocityDto[]> => {
    const response = await apiClient.get<SprintVelocityDto[]>(
      '/management/analytics/velocity',
      {
        params: { projectId, sprintsCount },
      }
    );
    return response.data;
  },

  getHoursComparison: async (projectId: string): Promise<HoursComparisonDto[]> => {
    const response = await apiClient.get<HoursComparisonDto[]>(
      '/management/analytics/hours-comparison',
      {
        params: { projectId },
      }
    );
    return response.data;
  },

  getCompletionRates: async (projectId: string): Promise<CompletionRateDto> => {
    const response = await apiClient.get<CompletionRateDto>(
      '/management/analytics/completion-rates',
      {
        params: { projectId },
      }
    );
    return response.data;
  },

  getBurndown: async (
    projectId: string,
    sprintName?: string
  ): Promise<BurndownDto> => {
    const response = await apiClient.get<BurndownDto>(
      '/management/analytics/burndown',
      {
        params: { projectId, sprintName },
      }
    );
    return response.data;
  },
};
