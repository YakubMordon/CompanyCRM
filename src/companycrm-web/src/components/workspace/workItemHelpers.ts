import {
  ReviewStatus,
  SyncOrigin,
  WorkItemDto,
  WorkItemHistoryDto,
  WorkItemPriority,
  WorkItemStatus,
  WorkItemType,
} from '../../types';

const WORK_ITEM_TYPE_BY_INDEX: readonly WorkItemType[] = ['Task', 'Bug', 'Story'];
const WORK_ITEM_STATUS_BY_INDEX: readonly WorkItemStatus[] = [
  'Draft',
  'PendingReview',
  'ClarificationRequested',
  'Denied',
  'Backlog',
  'Todo',
  'InProgress',
  'InVerify',
  'Done',
];
const WORK_ITEM_PRIORITY_BY_INDEX: readonly WorkItemPriority[] = [
  'Low',
  'Medium',
  'High',
  'Critical',
];
const REVIEW_STATUS_BY_INDEX: readonly ReviewStatus[] = [
  'NotRequired',
  'Pending',
  'Approved',
  'ClarificationNeeded',
  'Denied',
];
const SYNC_ORIGIN_BY_INDEX: readonly SyncOrigin[] = [
  'LocalCrm',
  'ExternalWebhook',
  'BackgroundReconciliation',
];

function normalizeEnumValue<T extends string>(
  value: unknown,
  orderedValues: readonly T[],
  fallback: T
): T {
  if (typeof value === 'number' && Number.isInteger(value)) {
    if (value >= 0 && value < orderedValues.length) {
      return orderedValues[value];
    }
    return fallback;
  }

  if (typeof value === 'string') {
    const trimmed = value.trim();
    if (/^\d+$/.test(trimmed)) {
      const idx = Number.parseInt(trimmed, 10);
      if (idx >= 0 && idx < orderedValues.length) {
        return orderedValues[idx];
      }
    }
    const exact = orderedValues.find((v) => v === trimmed);
    if (exact) return exact;
    const ci = orderedValues.find((v) => v.toLowerCase() === trimmed.toLowerCase());
    if (ci) return ci;
  }

  return fallback;
}

export function normalizeWorkItemStatus(value: unknown): WorkItemStatus {
  return normalizeEnumValue(value, WORK_ITEM_STATUS_BY_INDEX, 'Backlog');
}

export function normalizeReviewStatus(value: unknown): ReviewStatus {
  return normalizeEnumValue(value, REVIEW_STATUS_BY_INDEX, 'NotRequired');
}

export function normalizeWorkItemDto(raw: any): WorkItemDto {
  if (!raw) return raw;
  const totalLoggedMinutes = Number(raw.totalLoggedMinutes ?? 0);
  const billableMinutes = Number(raw.billableMinutes ?? 0);
  return {
    ...raw,
    type: normalizeEnumValue(raw.type, WORK_ITEM_TYPE_BY_INDEX, 'Task'),
    status: normalizeWorkItemStatus(raw.status),
    priority: normalizeEnumValue(raw.priority, WORK_ITEM_PRIORITY_BY_INDEX, 'Medium'),
    reviewStatus: normalizeReviewStatus(raw.reviewStatus),
    lastSyncOrigin: normalizeEnumValue(raw.lastSyncOrigin, SYNC_ORIGIN_BY_INDEX, 'LocalCrm'),
    creatorId: raw.creatorId ?? raw.createdById ?? '',
    creatorName: raw.creatorName ?? raw.createdByName ?? 'Unknown',
    reviewComment: raw.reviewComment ?? raw.reviewLatestComment ?? null,
    isLockedOnBoard: Boolean(raw.isLockedOnBoard),
    totalLoggedMinutes,
    billableMinutes,
    nonBillableMinutes:
      raw.nonBillableMinutes ?? Math.max(0, totalLoggedMinutes - billableMinutes),
  };
}

export function getHistoryActionLabel(h: Pick<WorkItemHistoryDto, 'action' | 'changeType'>): string {
  return h.action || h.changeType || 'Activity';
}

export function getHistoryNote(h: Pick<WorkItemHistoryDto, 'note' | 'notes'>): string | null {
  return (h.note ?? h.notes)?.trim() || null;
}

export function isWorkItemLockedOnBoard(
  item: Pick<WorkItemDto, 'isLockedOnBoard' | 'status' | 'reviewStatus'>
): boolean {
  const status = normalizeWorkItemStatus(item.status);
  const reviewStatus = normalizeReviewStatus(item.reviewStatus);
  return Boolean(
    item.isLockedOnBoard ||
      status === 'PendingReview' ||
      status === 'ClarificationRequested' ||
      status === 'Denied' ||
      reviewStatus === 'Pending' ||
      reviewStatus === 'ClarificationNeeded' ||
      reviewStatus === 'Denied'
  );
}

export function groupWorkItemsByColumn(
  workItems: WorkItemDto[]
): Record<WorkItemStatus, WorkItemDto[]> {
  const grouped: Record<WorkItemStatus, WorkItemDto[]> = {
    Backlog: [],
    Todo: [],
    InProgress: [],
    InVerify: [],
    Done: [],
    Draft: [],
    PendingReview: [],
    ClarificationRequested: [],
    Denied: [],
  };

  for (const item of workItems) {
    const normalizedStatus = normalizeWorkItemStatus(item.status);
    const normalizedItem =
      normalizedStatus === item.status ? item : { ...item, status: normalizedStatus };

    switch (normalizedStatus) {
      case 'Todo':
        grouped.Todo.push(normalizedItem);
        break;
      case 'InProgress':
        grouped.InProgress.push(normalizedItem);
        break;
      case 'InVerify':
        grouped.InVerify.push(normalizedItem);
        break;
      case 'Done':
        grouped.Done.push(normalizedItem);
        break;
      case 'Backlog':
      case 'Draft':
      case 'PendingReview':
      case 'ClarificationRequested':
      case 'Denied':
      default:
        grouped.Backlog.push(normalizedItem);
        break;
    }
  }

  return grouped;
}
