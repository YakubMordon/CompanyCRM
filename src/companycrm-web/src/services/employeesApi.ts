import apiClient from './apiClient';
import {
  EmployeeDirectoryDto,
  EmployeeProfileDto,
  EmployeeSkillDto,
  UpdateProfileRequestDto,
  UpsertSkillRequestDto,
} from '../types';

export const employeesApi = {
  getEmployees: async (search?: string, department?: string): Promise<EmployeeDirectoryDto[]> => {
    const response = await apiClient.get<EmployeeDirectoryDto[]>('/employees', {
      params: { search, department },
    });
    return response.data;
  },

  getEmployeeProfile: async (id: string): Promise<EmployeeProfileDto> => {
    const response = await apiClient.get<EmployeeProfileDto>(`/employees/${id}`);
    return response.data;
  },

  updateProfile: async (
    id: string,
    dto: UpdateProfileRequestDto
  ): Promise<EmployeeProfileDto> => {
    const response = await apiClient.put<EmployeeProfileDto>(`/employees/${id}/profile`, dto);
    return response.data;
  },

  getEmployeeSkills: async (id: string): Promise<EmployeeSkillDto[]> => {
    const response = await apiClient.get<EmployeeSkillDto[]>(`/employees/${id}/skills`);
    return response.data;
  },

  upsertSkill: async (id: string, dto: UpsertSkillRequestDto): Promise<EmployeeSkillDto> => {
    const response = await apiClient.post<EmployeeSkillDto>(`/employees/${id}/skills`, dto);
    return response.data;
  },

  deleteSkill: async (id: string, skillName: string): Promise<void> => {
    await apiClient.delete(`/employees/${id}/skills/${encodeURIComponent(skillName)}`);
  },
};
