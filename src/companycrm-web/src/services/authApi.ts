import { postApiAuthLogin, getApiAuthMe, postApiAuthSwitchPersonaRole } from '../api/generated';
import { AppRole, AuthResponseDto, CurrentUserDto, LoginRequest } from '../types';

export const authApi = {
  login: async (request: LoginRequest): Promise<AuthResponseDto> => {
    return (await postApiAuthLogin(request)) as AuthResponseDto;
  },

  getCurrentUser: async (): Promise<CurrentUserDto> => {
    return (await getApiAuthMe()) as CurrentUserDto;
  },

  switchPersona: async (role: AppRole): Promise<AuthResponseDto> => {
    return (await postApiAuthSwitchPersonaRole(role)) as AuthResponseDto;
  },
};
