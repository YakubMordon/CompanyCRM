import {
  getApiWorkitems,
  getApiWorkitemsId,
  postApiWorkitems,
  putApiWorkitemsId,
  patchApiWorkitemsIdStatus,
  postApiWorkitemsIdTimerStart,
  postApiWorkitemsIdTimerStop,
  getApiWorkitemsActiveTimer,
  postApiWorkitemsIdTimeEntries,
  getApiWorkitemsIdHistory,
} from '../api/generated';
import {
  CreateManualTimeEntryDto,
  CreateWorkItemDto,
  KanbanTransitionDto,
  PagedResult,
  StartTimerRequestDto,
  StopTimerRequestDto,
  TimeEntryDto,
  UpdateWorkItemDto,
  WorkItemDto,
  WorkItemHistoryDto,
  WorkItemPriority,
  WorkItemStatus,
  WorkItemType,
} from '../types';

export interface WorkItemQueryParams {
  projectId?: string;
  status?: WorkItemStatus;
  type?: WorkItemType;
  priority?: WorkItemPriority;
  assigneeId?: string;
  reviewerId?: string;
  sprint?: string;
  search?: string;
  page?: number;
  pageSize?: number;
}

export function normalizeWorkItemHistoryDto(raw: any): WorkItemHistoryDto {
  if (!raw) return raw;
  const action = raw.action || raw.changeType || 'Activity';
  const note = raw.note ?? raw.notes ?? null;
  return {
    ...raw,
    action,
    changeType: raw.changeType || action,
    note,
    notes: raw.notes ?? note,
  };
}

export const workItemsApi = {
  getWorkItems: async (params?: WorkItemQueryParams): Promise<PagedResult<WorkItemDto>> => {
    return (await getApiWorkitems(params as any)) as PagedResult<WorkItemDto>;
  },

  getWorkItemById: async (id: string): Promise<WorkItemDto> => {
    return (await getApiWorkitemsId(id)) as WorkItemDto;
  },

  createWorkItem: async (dto: CreateWorkItemDto): Promise<WorkItemDto> => {
    return (await postApiWorkitems(dto as any)) as WorkItemDto;
  },

  updateWorkItem: async (id: string, dto: UpdateWorkItemDto): Promise<WorkItemDto> => {
    return (await putApiWorkitemsId(id, dto as any)) as WorkItemDto;
  },

  transitionStatus: async (id: string, dto: KanbanTransitionDto): Promise<WorkItemDto> => {
    return (await patchApiWorkitemsIdStatus(id, dto)) as WorkItemDto;
  },

  startTimer: async (id: string, dto?: StartTimerRequestDto): Promise<TimeEntryDto> => {
    return (await postApiWorkitemsIdTimerStart(id, dto || {})) as TimeEntryDto;
  },

  stopTimer: async (id: string, dto?: StopTimerRequestDto): Promise<TimeEntryDto> => {
    return (await postApiWorkitemsIdTimerStop(id, dto || {})) as TimeEntryDto;
  },

  getActiveTimer: async (): Promise<TimeEntryDto | null> => {
    const data = await getApiWorkitemsActiveTimer();
    if (!data) return null;
    return data as TimeEntryDto;
  },

  createManualTimeEntry: async (
    id: string,
    dto: CreateManualTimeEntryDto
  ): Promise<TimeEntryDto> => {
    return (await postApiWorkitemsIdTimeEntries(id, dto)) as TimeEntryDto;
  },

  getWorkItemHistory: async (id: string): Promise<WorkItemHistoryDto[]> => {
    const rawData = await getApiWorkitemsIdHistory(id);
    const items = Array.isArray(rawData) ? rawData : [];
    return items.map(normalizeWorkItemHistoryDto);
  },
};
