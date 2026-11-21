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
    projectId?: string,
    startDate?: string,
    endDate?: string
  ): Promise<ProjectProfitabilityDto> => {
    return (await getApiManagementProfitability({
      projectId: projectId || undefined,
      startDate,
      endDate,
    } as any)) as ProjectProfitabilityDto;
  },

  getVelocity: async (
    projectId?: string,
    sprintsCount = 5
  ): Promise<SprintVelocityDto[]> => {
    return (await getApiManagementAnalyticsVelocity({
      projectId: projectId || undefined,
      sprintsCount,
    } as any)) as SprintVelocityDto[];
  },

  getHoursComparison: async (projectId?: string): Promise<HoursComparisonDto[]> => {
    return (await getApiManagementAnalyticsHoursComparison({
      projectId: projectId || undefined,
    } as any)) as HoursComparisonDto[];
  },

  getCompletionRates: async (projectId?: string): Promise<CompletionRateDto> => {
    return (await getApiManagementAnalyticsCompletionRates({
      projectId: projectId || undefined,
    } as any)) as CompletionRateDto;
  },

  getBurndown: async (
    projectId?: string,
    sprintName?: string
  ): Promise<BurndownDto> => {
    return (await getApiManagementAnalyticsBurndown({
      projectId: projectId || undefined,
      sprintName,
    } as any)) as BurndownDto;
  },
};
