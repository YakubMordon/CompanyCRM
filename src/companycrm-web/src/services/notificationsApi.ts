import { apiClient } from './apiClient';

export interface ChatWebhookConfigDto {
  id: string;
  projectId?: string | null;
  slackWebhookUrl?: string | null;
  teamsWebhookUrl?: string | null;
  isCutoffAlertsEnabled: boolean;
  isReviewAlertsEnabled: boolean;
  isSimulatedMode: boolean;
  updatedAt: string;
}

export interface UpdateChatWebhookConfigDto {
  projectId?: string | null;
  slackWebhookUrl?: string | null;
  teamsWebhookUrl?: string | null;
  isCutoffAlertsEnabled: boolean;
  isReviewAlertsEnabled: boolean;
  isSimulatedMode: boolean;
}

export interface ChatWebhookTestResultDto {
  slackDelivered: boolean;
  teamsDelivered: boolean;
  isSimulated: boolean;
  message: string;
}

export const notificationsApi = {
  getWebhookConfig: async (projectId?: string): Promise<ChatWebhookConfigDto> => {
    const response = await apiClient.get<ChatWebhookConfigDto>('/api/notifications/webhooks', {
      params: projectId ? { projectId } : undefined,
    });
    return response.data;
  },

  updateWebhookConfig: async (dto: UpdateChatWebhookConfigDto): Promise<ChatWebhookConfigDto> => {
    const response = await apiClient.put<ChatWebhookConfigDto>('/api/notifications/webhooks', dto);
    return response.data;
  },

  sendTestAlert: async (projectId?: string): Promise<ChatWebhookTestResultDto> => {
    const response = await apiClient.post<ChatWebhookTestResultDto>(
      '/api/notifications/webhooks/test',
      null,
      { params: projectId ? { projectId } : undefined }
    );
    return response.data;
  },
};
