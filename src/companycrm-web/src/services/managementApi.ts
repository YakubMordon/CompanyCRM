import {
  getApiManagementCompensation,
  getApiManagementProfitability,
  getApiManagementAnalyticsVelocity,
  getApiManagementAnalyticsHoursComparison,
  getApiManagementAnalyticsCompletionRates,
  getApiManagementAnalyticsBurndown,
} from '../api/generated';
import { customInstance } from '../api/mutator';
import {
  BurndownDto,
  CompletionRateDto,
  CreateEmployeeRequestDto,
  EmployeeCompensationDto,
  HoursComparisonDto,
  ProjectProfitabilityDto,
  SprintVelocityDto,
  UpdateEmployeeRequestDto,
} from '../types';

export const managementApi = {
  getCompensation: async (): Promise<EmployeeCompensationDto[]> => {
    return (await getApiManagementCompensation()) as EmployeeCompensationDto[];
  },

  createEmployee: async (dto: CreateEmployeeRequestDto): Promise<EmployeeCompensationDto> => {
    return await customInstance<EmployeeCompensationDto>({
      url: '/api/management/employees',
      method: 'POST',
      data: dto,
    });
  },

  updateEmployee: async (
    id: string,
    dto: UpdateEmployeeRequestDto
  ): Promise<EmployeeCompensationDto> => {
    return await customInstance<EmployeeCompensationDto>({
      url: `/api/management/employees/${id}`,
      method: 'PUT',
      data: dto,
    });
  },

  deleteEmployee: async (id: string): Promise<void> => {
    await customInstance<void>({
      url: `/api/management/employees/${id}`,
      method: 'DELETE',
    });
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
