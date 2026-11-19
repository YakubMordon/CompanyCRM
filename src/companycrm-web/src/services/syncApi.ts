import apiClient from './apiClient';
import {
  ConflictResolutionResultDto,
  ReconciliationResultDto,
  ResolveConflictRequestDto,
  SyncConflictDto,
  SyncResultDto,
} from '../types';

export function normalizeReconciliationResultDto(raw: any): ReconciliationResultDto {
  if (!raw) return raw;
  const totalItemsChecked = raw.totalItemsChecked ?? raw.fetchedCount ?? 0;
  const updatedCount = raw.updatedCount ?? 0;
  const conflictsCount = raw.conflictsCount ?? raw.conflictsDetectedCount ?? 0;
  return {
    ...raw,
    projectId: raw.projectId ?? null,
    totalItemsChecked,
    updatedCount,
    conflictsCount,
    skippedCount: raw.skippedCount ?? 0,
    errors: Array.isArray(raw.errors) ? raw.errors : [],
    startedAt: raw.startedAt || new Date().toISOString(),
    completedAt: raw.completedAt || new Date().toISOString(),
    fetchedCount: raw.fetchedCount ?? totalItemsChecked,
    conflictsDetectedCount: raw.conflictsDetectedCount ?? conflictsCount,
  };
}

export const syncApi = {
  reconcileProject: async (projectId: string): Promise<ReconciliationResultDto> => {
    const response = await apiClient.post<ReconciliationResultDto>(
      `/sync/projects/${projectId}/reconcile`
    );
    return normalizeReconciliationResultDto(response.data);
  },

  pushWorkItem: async (id: string): Promise<SyncResultDto> => {
    const response = await apiClient.post<SyncResultDto>(`/sync/workitems/${id}/push`);
    return response.data;
  },

  getConflicts: async (projectId?: string): Promise<SyncConflictDto[]> => {
    const response = await apiClient.get<SyncConflictDto[]>('/sync/conflicts', {
      params: { projectId },
    });
    return response.data;
  },

  resolveConflict: async (
    conflictId: string,
    dto: ResolveConflictRequestDto
  ): Promise<ConflictResolutionResultDto> => {
    const response = await apiClient.post<ConflictResolutionResultDto>(
      `/sync/conflicts/${conflictId}/resolve`,
      dto
    );
    return response.data;
  },
};
