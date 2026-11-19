import apiClient from './apiClient';
import {
  ApproveReviewRequestDto,
  ClarifyReviewRequestDto,
  DenyReviewRequestDto,
  WorkItemDto,
  WorkItemReviewQueueDto,
} from '../types';

/**
 * Normalizes raw backend or frontend review queue item to ensure all
 * alias and canonical properties (`id`, `workItemId`, `code`, `title`, etc.)
 * are populated and never undefined.
 */
export function normalizeWorkItemReviewQueueDto(raw: any): WorkItemReviewQueueDto {
  if (!raw) return raw;
  const id = raw.id || raw.workItemId || '';
  const code = raw.code || raw.workItemCode || '';
  const title = raw.title || raw.workItemTitle || '';
  const creatorId = raw.creatorId || raw.createdById || '';
  const creatorName = raw.creatorName || raw.createdByName || 'Unknown';
  const reviewerId = raw.reviewerId ?? null;
  const reviewerName = raw.reviewerName ?? 'Unassigned';
  const reviewComment = raw.reviewComment ?? raw.latestComment ?? null;
  const requestedAt = raw.requestedAt || raw.createdAt || new Date().toISOString();

  return {
    ...raw,
    id,
    workItemId: raw.workItemId || id,
    code,
    workItemCode: raw.workItemCode || code,
    title,
    workItemTitle: raw.workItemTitle || title,
    descriptionPreview: raw.descriptionPreview ?? null,
    projectId: raw.projectId || '',
    projectCode: raw.projectCode || '',
    projectName: raw.projectName || '',
    type: raw.type,
    priority: raw.priority,
    creatorId,
    createdById: raw.createdById || creatorId,
    creatorName,
    createdByName: raw.createdByName || creatorName,
    reviewerId,
    reviewerName,
    reviewStatus: raw.reviewStatus,
    reviewComment,
    latestComment: raw.latestComment ?? reviewComment,
    requestedAt,
    createdAt: raw.createdAt || requestedAt,
  };
}

export const reviewsApi = {
  getReviewQueue: async (
    projectId?: string,
    assignedToMeOnly = false
  ): Promise<WorkItemReviewQueueDto[]> => {
    const response = await apiClient.get<any[]>('/reviews/queue', {
      params: { projectId, assignedToMeOnly },
    });
    const items = Array.isArray(response.data) ? response.data : [];
    return items.map(normalizeWorkItemReviewQueueDto);
  },

  approveReview: async (id: string, dto?: ApproveReviewRequestDto): Promise<WorkItemDto> => {
    if (!id || id === 'undefined') {
      throw new Error('Valid review work item ID is required to approve review.');
    }
    const response = await apiClient.post<WorkItemDto>(`/reviews/${id}/approve`, dto || {});
    return response.data;
  },

  requestClarification: async (
    id: string,
    dto: ClarifyReviewRequestDto
  ): Promise<WorkItemDto> => {
    if (!id || id === 'undefined') {
      throw new Error('Valid review work item ID is required to request clarification.');
    }
    const response = await apiClient.post<WorkItemDto>(`/reviews/${id}/clarify`, dto);
    return response.data;
  },

  denyReview: async (id: string, dto: DenyReviewRequestDto): Promise<WorkItemDto> => {
    if (!id || id === 'undefined') {
      throw new Error('Valid review work item ID is required to deny review.');
    }
    const response = await apiClient.post<WorkItemDto>(`/reviews/${id}/deny`, dto);
    return response.data;
  },
};
