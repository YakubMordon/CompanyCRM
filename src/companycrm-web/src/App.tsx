import React from 'react';
import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { AppThemeProvider } from './contexts/ThemeContext';
import { AuthProvider } from './contexts/AuthContext';
import { ProjectProvider } from './contexts/ProjectContext';
import { SignalRProvider } from './contexts/SignalRContext';
import { AppShell } from './components/layout/AppShell';
import { WorkspacePage } from './pages/WorkspacePage';
import { ReviewQueuePage } from './pages/ReviewQueuePage';
import { ExternalSyncPage } from './pages/ExternalSyncPage';
import { EmployeesDirectoryPage } from './pages/EmployeesDirectoryPage';
import { UserProfilePage } from './pages/UserProfilePage';
import { PlanningPage } from './pages/PlanningPage';
import { ManagementPortalPage } from './pages/ManagementPortalPage';

export const App: React.FC = () => {
  return (
    <AppThemeProvider>
      <AuthProvider>
        <ProjectProvider>
          <SignalRProvider>
            <BrowserRouter>
              <Routes>
                <Route path="/" element={<AppShell />}>
                  <Route index element={<WorkspacePage />} />
                  <Route path="workspace" element={<WorkspacePage />} />
                  <Route path="reviews" element={<ReviewQueuePage />} />
                  <Route path="sync" element={<ExternalSyncPage />} />
                  <Route path="employees" element={<EmployeesDirectoryPage />} />
                  <Route path="profile" element={<UserProfilePage />} />
                  <Route path="planning" element={<PlanningPage />} />
                  <Route path="management" element={<ManagementPortalPage />} />
                  <Route path="*" element={<Navigate to="/" replace />} />
                </Route>
              </Routes>
            </BrowserRouter>
          </SignalRProvider>
        </ProjectProvider>
      </AuthProvider>
    </AppThemeProvider>
  );
};

export default App;
