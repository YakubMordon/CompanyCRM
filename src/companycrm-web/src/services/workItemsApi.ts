import apiClient from './apiClient';
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
    const response = await apiClient.get<PagedResult<WorkItemDto>>('/workitems', {
      params,
    });
    return response.data;
  },

  getWorkItemById: async (id: string): Promise<WorkItemDto> => {
    const response = await apiClient.get<WorkItemDto>(`/workitems/${id}`);
    return response.data;
  },

  createWorkItem: async (dto: CreateWorkItemDto): Promise<WorkItemDto> => {
    const response = await apiClient.post<WorkItemDto>('/workitems', dto);
    return response.data;
  },

  updateWorkItem: async (id: string, dto: UpdateWorkItemDto): Promise<WorkItemDto> => {
    const response = await apiClient.put<WorkItemDto>(`/workitems/${id}`, dto);
    return response.data;
  },

  transitionStatus: async (id: string, dto: KanbanTransitionDto): Promise<WorkItemDto> => {
    const response = await apiClient.patch<WorkItemDto>(`/workitems/${id}/status`, dto);
    return response.data;
  },

  startTimer: async (id: string, dto?: StartTimerRequestDto): Promise<TimeEntryDto> => {
    const response = await apiClient.post<TimeEntryDto>(`/workitems/${id}/timer/start`, dto || {});
    return response.data;
  },

  stopTimer: async (id: string, dto?: StopTimerRequestDto): Promise<TimeEntryDto> => {
    const response = await apiClient.post<TimeEntryDto>(`/workitems/${id}/timer/stop`, dto || {});
    return response.data;
  },

  getActiveTimer: async (): Promise<TimeEntryDto | null> => {
    const response = await apiClient.get<TimeEntryDto | null>('/workitems/active-timer');
    if (response.status === 204 || !response.data) {
      return null;
    }
    return response.data;
  },

  createManualTimeEntry: async (
    id: string,
    dto: CreateManualTimeEntryDto
  ): Promise<TimeEntryDto> => {
    const response = await apiClient.post<TimeEntryDto>(`/workitems/${id}/time-entries`, dto);
    return response.data;
  },

  getWorkItemHistory: async (id: string): Promise<WorkItemHistoryDto[]> => {
    const response = await apiClient.get<WorkItemHistoryDto[]>(`/workitems/${id}/history`);
    const items = Array.isArray(response.data) ? response.data : [];
    return items.map(normalizeWorkItemHistoryDto);
  },
};
