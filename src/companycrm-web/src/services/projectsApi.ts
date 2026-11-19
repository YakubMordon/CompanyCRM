import apiClient from './apiClient';
import {
  AddProjectMemberDto,
  CreateProjectDto,
  ProjectDto,
  ProjectExternalConfigDto,
  ProjectMemberDto,
  UpdateProjectDto,
  UpdateProjectExternalConfigDto,
} from '../types';

export const projectsApi = {
  getProjects: async (includeInactive = false): Promise<ProjectDto[]> => {
    const response = await apiClient.get<ProjectDto[]>('/projects', {
      params: { includeInactive },
    });
    return response.data;
  },

  getProjectById: async (id: string): Promise<ProjectDto> => {
    const response = await apiClient.get<ProjectDto>(`/projects/${id}`);
    return response.data;
  },

  createProject: async (dto: CreateProjectDto): Promise<ProjectDto> => {
    const response = await apiClient.post<ProjectDto>('/projects', dto);
    return response.data;
  },

  updateProject: async (id: string, dto: UpdateProjectDto): Promise<ProjectDto> => {
    const response = await apiClient.put<ProjectDto>(`/projects/${id}`, dto);
    return response.data;
  },

  getProjectMembers: async (id: string): Promise<ProjectMemberDto[]> => {
    const response = await apiClient.get<ProjectMemberDto[]>(`/projects/${id}/members`);
    return response.data;
  },

  addProjectMember: async (id: string, dto: AddProjectMemberDto): Promise<ProjectMemberDto> => {
    const response = await apiClient.post<ProjectMemberDto>(`/projects/${id}/members`, dto);
    return response.data;
  },

  removeProjectMember: async (id: string, employeeId: string): Promise<void> => {
    await apiClient.delete(`/projects/${id}/members/${employeeId}`);
  },

  getExternalConfig: async (id: string): Promise<ProjectExternalConfigDto> => {
    const response = await apiClient.get<ProjectExternalConfigDto>(`/projects/${id}/external-config`);
    return response.data;
  },

  updateExternalConfig: async (
    id: string,
    dto: UpdateProjectExternalConfigDto
  ): Promise<ProjectExternalConfigDto> => {
    const response = await apiClient.put<ProjectExternalConfigDto>(
      `/projects/${id}/external-config`,
      dto
    );
    return response.data;
  },
};
