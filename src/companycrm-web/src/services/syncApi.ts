import {
  postApiSyncProjectsIdReconcile,
  postApiSyncWorkitemsIdPush,
  getApiSyncConflicts,
  postApiSyncConflictsIdResolve,
} from '../api/generated';
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
    const rawData = await postApiSyncProjectsIdReconcile(projectId);
    return normalizeReconciliationResultDto(rawData);
  },

  pushWorkItem: async (id: string): Promise<SyncResultDto> => {
    return (await postApiSyncWorkitemsIdPush(id)) as SyncResultDto;
  },

  getConflicts: async (projectId?: string): Promise<SyncConflictDto[]> => {
    return (await getApiSyncConflicts({ projectId })) as SyncConflictDto[];
  },

  resolveConflict: async (
    conflictId: string,
    dto: ResolveConflictRequestDto
  ): Promise<ConflictResolutionResultDto> => {
    return (await postApiSyncConflictsIdResolve(conflictId, dto)) as ConflictResolutionResultDto;
  },
};
