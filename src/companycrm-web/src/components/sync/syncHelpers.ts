import { ReconciliationResultDto, SyncEventDto } from '../../types';

export function normalizeSyncEvent(rawEvent: Partial<SyncEventDto>): SyncEventDto {
  const status = rawEvent.status || rawEvent.eventType || 'SyncEvent';
  return {
    projectId: rawEvent.projectId || '',
    workItemId: rawEvent.workItemId,
    externalId: rawEvent.externalId,
    status,
    eventType: status,
    message: rawEvent.message || '',
    timestamp: rawEvent.timestamp || new Date().toISOString(),
  };
}

export function getSyncChipColor(
  status: string
): 'success' | 'warning' | 'error' | 'info' | 'default' {
  switch (status) {
    case 'WorkItemPushed':
    case 'WorkItemImported':
    case 'SyncCompleted':
      return 'success';
    case 'ConflictDetected':
      return 'warning';
    case 'SyncFailed':
      return 'error';
    case 'SyncStarted':
      return 'info';
    default:
      return 'default';
  }
}

export function canMergeConflict(conflict: { localWorkItemId?: string | null }): boolean {
  return Boolean(conflict.localWorkItemId);
}

export function formatReconciliationSummary(res: ReconciliationResultDto): {
  severity: 'success' | 'error';
  message: string;
} {
  const totalChecked = res.totalItemsChecked ?? res.fetchedCount ?? 0;
  const updatedCount = res.updatedCount ?? 0;
  const conflictsCount = res.conflictsCount ?? res.conflictsDetectedCount ?? 0;
  const hasErrors = Array.isArray(res.errors) && res.errors.length > 0;

  return {
    severity: hasErrors ? 'error' : 'success',
    message: hasErrors
      ? `Reconciliation completed with errors (${totalChecked} checked, ${updatedCount} updated, ${conflictsCount} conflicts): ${res.errors.join('; ')}`
      : `Reconciliation completed: ${totalChecked} checked, ${updatedCount} updated, ${conflictsCount} conflicts detected.`,
  };
}
