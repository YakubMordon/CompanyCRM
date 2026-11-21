import React, { createContext, useContext, useEffect, useState, useCallback } from 'react';
import { ProjectDto } from '../types';
import { projectsApi } from '../services/projectsApi';
import { useAuth } from './AuthContext';
import { resolveActiveProject } from './contextHelpers';

interface ProjectContextType {
  projects: ProjectDto[];
  activeProject: ProjectDto | null;
  isLoading: boolean;
  setActiveProject: (project: ProjectDto | null) => void;
  refreshProjects: () => Promise<void>;
}

const ProjectContext = createContext<ProjectContextType | undefined>(undefined);

export const ProjectProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const { token } = useAuth();
  const [projects, setProjects] = useState<ProjectDto[]>([]);
  const [activeProject, setActiveProjectState] = useState<ProjectDto | null>(null);
  const [isLoading, setIsLoading] = useState<boolean>(true);

  const setActiveProject = useCallback((project: ProjectDto | null) => {
    setActiveProjectState(project);
    if (project) {
      localStorage.setItem('crm_active_project_id', project.id);
    } else {
      localStorage.setItem('crm_active_project_id', 'ALL');
    }
  }, []);

  const refreshProjects = useCallback(async () => {
    if (!token) return;
    try {
      setIsLoading(true);
      const data = await projectsApi.getProjects();
      setProjects(data);

      const savedId = localStorage.getItem('crm_active_project_id');
      const matched = resolveActiveProject(data, savedId);
      setActiveProjectState(matched);
    } catch (err) {
      console.error('Failed to load projects:', err);
    } finally {
      setIsLoading(false);
    }
  }, [token]);

  useEffect(() => {
    if (token) {
      refreshProjects();
    }
  }, [token, refreshProjects]);

  return (
    <ProjectContext.Provider
      value={{
        projects,
        activeProject,
        isLoading,
        setActiveProject,
        refreshProjects,
      }}
    >
      {children}
    </ProjectContext.Provider>
  );
};

export const useProject = (): ProjectContextType => {
  const context = useContext(ProjectContext);
  if (!context) {
    throw new Error('useProject must be used within a ProjectProvider');
  }
  return context;
};
