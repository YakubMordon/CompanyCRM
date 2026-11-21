import {
  getApiEmployees,
  getApiEmployeesIdProfile,
  putApiEmployeesIdProfile,
  getApiEmployeesIdSkills,
  postApiEmployeesIdSkills,
  deleteApiEmployeesIdSkillsSkillName,
} from '../api/generated';
import {
  EmployeeDirectoryDto,
  EmployeeProfileDto,
  EmployeeSkillDto,
  UpdateProfileRequestDto,
  UpsertSkillRequestDto,
} from '../types';

export const employeesApi = {
  getEmployees: async (search?: string, department?: string, projectId?: string): Promise<EmployeeDirectoryDto[]> => {
    return (await getApiEmployees({ search, department, projectId: projectId || undefined } as any)) as EmployeeDirectoryDto[];
  },

  getEmployeeProfile: async (id: string): Promise<EmployeeProfileDto> => {
    return (await getApiEmployeesIdProfile(id)) as EmployeeProfileDto;
  },

  updateProfile: async (
    id: string,
    dto: UpdateProfileRequestDto
  ): Promise<EmployeeProfileDto> => {
    return (await putApiEmployeesIdProfile(id, dto)) as EmployeeProfileDto;
  },

  getEmployeeSkills: async (id: string): Promise<EmployeeSkillDto[]> => {
    return (await getApiEmployeesIdSkills(id)) as EmployeeSkillDto[];
  },

  upsertSkill: async (id: string, dto: UpsertSkillRequestDto): Promise<EmployeeSkillDto> => {
    return (await postApiEmployeesIdSkills(id, dto)) as EmployeeSkillDto;
  },

  deleteSkill: async (id: string, skillName: string): Promise<void> => {
    await deleteApiEmployeesIdSkillsSkillName(id, skillName);
  },
};
