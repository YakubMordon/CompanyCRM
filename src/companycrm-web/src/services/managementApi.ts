import {
  getApiManagementCompensation,
  getApiManagementProfitability,
  getApiManagementAnalyticsVelocity,
  getApiManagementAnalyticsHoursComparison,
  getApiManagementAnalyticsCompletionRates,
  getApiManagementAnalyticsBurndown,
} from '../api/generated';
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
    return (await getApiManagementCompensation()) as EmployeeCompensationDto[];
  },

  getProfitability: async (
    projectId: string,
    startDate?: string,
    endDate?: string
  ): Promise<ProjectProfitabilityDto> => {
    return (await getApiManagementProfitability({
      projectId,
      startDate,
      endDate,
    })) as ProjectProfitabilityDto;
  },

  getVelocity: async (
    projectId: string,
    sprintsCount = 5
  ): Promise<SprintVelocityDto[]> => {
    return (await getApiManagementAnalyticsVelocity({
      projectId,
      sprintsCount,
    })) as SprintVelocityDto[];
  },

  getHoursComparison: async (projectId: string): Promise<HoursComparisonDto[]> => {
    return (await getApiManagementAnalyticsHoursComparison({
      projectId,
    })) as HoursComparisonDto[];
  },

  getCompletionRates: async (projectId: string): Promise<CompletionRateDto> => {
    return (await getApiManagementAnalyticsCompletionRates({
      projectId,
    })) as CompletionRateDto;
  },

  getBurndown: async (
    projectId: string,
    sprintName?: string
  ): Promise<BurndownDto> => {
    return (await getApiManagementAnalyticsBurndown({
      projectId,
      sprintName,
    })) as BurndownDto;
  },
};
