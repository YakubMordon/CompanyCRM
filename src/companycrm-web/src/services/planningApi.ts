import {
  getApiPlanningWeekly,
  postApiPlanningWeekly,
  getApiPlanningDaily,
  postApiPlanningDaily,
  getApiPlanningCutoffConfig,
  putApiPlanningCutoffConfig,
  postApiPlanningCutoffCheckRunNow,
} from '../api/generated';
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
    return (await getApiPlanningWeekly(params)) as WeeklyPlanDto[];
  },

  createOrUpdateWeeklyPlan: async (
    dto: CreateOrUpdateWeeklyPlanDto
  ): Promise<WeeklyPlanDto> => {
    return (await postApiPlanningWeekly(dto)) as WeeklyPlanDto;
  },

  getDailyPlans: async (params?: {
    date?: string;
    employeeId?: string;
  }): Promise<DailyPlanDto[]> => {
    return (await getApiPlanningDaily(params)) as DailyPlanDto[];
  },

  submitDailyPlan: async (dto: SubmitDailyPlanRequestDto): Promise<DailyPlanDto> => {
    return (await postApiPlanningDaily(dto)) as DailyPlanDto;
  },

  getCutoffConfig: async (projectId?: string): Promise<PlanningCutoffConfigDto> => {
    return (await getApiPlanningCutoffConfig({ projectId })) as PlanningCutoffConfigDto;
  },

  updateCutoffConfig: async (
    dto: UpdatePlanningCutoffConfigDto
  ): Promise<PlanningCutoffConfigDto> => {
    return (await putApiPlanningCutoffConfig(dto)) as PlanningCutoffConfigDto;
  },

  runCutoffCheckNow: async (projectId?: string | null): Promise<CutoffExecutionSummaryDto> => {
    return (await postApiPlanningCutoffCheckRunNow(
      projectId ? { projectId } : undefined
    )) as CutoffExecutionSummaryDto;
  },
};
