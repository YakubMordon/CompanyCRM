import apiClient from './apiClient';
import { AppRole, AuthResponseDto, CurrentUserDto, LoginRequest } from '../types';

export const authApi = {
  login: async (request: LoginRequest): Promise<AuthResponseDto> => {
    const response = await apiClient.post<AuthResponseDto>('/auth/login', request);
    return response.data;
  },

  getCurrentUser: async (): Promise<CurrentUserDto> => {
    const response = await apiClient.get<CurrentUserDto>('/auth/me');
    return response.data;
  },

  switchPersona: async (role: AppRole): Promise<AuthResponseDto> => {
    const response = await apiClient.post<AuthResponseDto>(`/auth/switch-persona/${role}`);
    return response.data;
  },
};
