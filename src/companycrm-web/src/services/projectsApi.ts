import {
  getApiProjects,
  getApiProjectsId,
  postApiProjects,
  putApiProjectsId,
  getApiProjectsIdMembers,
  postApiProjectsIdMembers,
  deleteApiProjectsIdMembersEmployeeId,
  getApiProjectsIdExternalConfig,
  putApiProjectsIdExternalConfig,
} from '../api/generated';
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
    return (await getApiProjects({ includeInactive })) as ProjectDto[];
  },

  getProjectById: async (id: string): Promise<ProjectDto> => {
    return (await getApiProjectsId(id)) as ProjectDto;
  },

  createProject: async (dto: CreateProjectDto): Promise<ProjectDto> => {
    return (await postApiProjects(dto)) as ProjectDto;
  },

  updateProject: async (id: string, dto: UpdateProjectDto): Promise<ProjectDto> => {
    return (await putApiProjectsId(id, dto)) as ProjectDto;
  },

  getProjectMembers: async (id: string): Promise<ProjectMemberDto[]> => {
    return (await getApiProjectsIdMembers(id)) as ProjectMemberDto[];
  },

  addProjectMember: async (id: string, dto: AddProjectMemberDto): Promise<ProjectMemberDto> => {
    return (await postApiProjectsIdMembers(id, dto)) as ProjectMemberDto;
  },

  removeProjectMember: async (id: string, employeeId: string): Promise<void> => {
    await deleteApiProjectsIdMembersEmployeeId(id, employeeId);
  },

  getExternalConfig: async (id: string): Promise<ProjectExternalConfigDto> => {
    return (await getApiProjectsIdExternalConfig(id)) as ProjectExternalConfigDto;
  },

  updateExternalConfig: async (
    id: string,
    dto: UpdateProjectExternalConfigDto
  ): Promise<ProjectExternalConfigDto> => {
    return (await putApiProjectsIdExternalConfig(id, dto)) as ProjectExternalConfigDto;
  },
};
