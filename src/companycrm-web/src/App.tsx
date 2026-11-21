import React from 'react';
import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { AppThemeProvider } from './contexts/ThemeContext';
import { AuthProvider } from './contexts/AuthContext';
import { ProjectProvider } from './contexts/ProjectContext';
import { SignalRProvider } from './contexts/SignalRContext';
import { AppShell } from './components/layout/AppShell';
import { ProtectedRoute } from './components/common/ProtectedRoute';
import { WorkspacePage } from './pages/WorkspacePage';
import { ReviewQueuePage } from './pages/ReviewQueuePage';
import { ExternalSyncPage } from './pages/ExternalSyncPage';
import { EmployeesDirectoryPage } from './pages/EmployeesDirectoryPage';
import { UserProfilePage } from './pages/UserProfilePage';
import { PlanningPage } from './pages/PlanningPage';
import { ManagementPortalPage } from './pages/ManagementPortalPage';

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      refetchOnWindowFocus: false,
      staleTime: 1000 * 60 * 2, // 2 minutes
    },
  },
});

export const App: React.FC = () => {
  return (
    <QueryClientProvider client={queryClient}>
      <AppThemeProvider>
        <AuthProvider>
          <ProjectProvider>
            <SignalRProvider>
              <BrowserRouter>
                <Routes>
                  <Route path="/" element={<AppShell />}>
                    <Route index element={<WorkspacePage />} />
                    <Route path="workspace" element={<WorkspacePage />} />
                    <Route
                      path="reviews"
                      element={
                        <ProtectedRoute path="/reviews">
                          <ReviewQueuePage />
                        </ProtectedRoute>
                      }
                    />
                    <Route
                      path="sync"
                      element={
                        <ProtectedRoute path="/sync">
                          <ExternalSyncPage />
                        </ProtectedRoute>
                      }
                    />
                    <Route
                      path="employees"
                      element={
                        <ProtectedRoute path="/employees">
                          <EmployeesDirectoryPage />
                        </ProtectedRoute>
                      }
                    />
                    <Route
                      path="profile"
                      element={
                        <ProtectedRoute path="/profile">
                          <UserProfilePage />
                        </ProtectedRoute>
                      }
                    />
                    <Route
                      path="planning"
                      element={
                        <ProtectedRoute path="/planning">
                          <PlanningPage />
                        </ProtectedRoute>
                      }
                    />
                    <Route
                      path="management"
                      element={
                        <ProtectedRoute path="/management">
                          <ManagementPortalPage />
                        </ProtectedRoute>
                      }
                    />
                    <Route path="*" element={<Navigate to="/" replace />} />
                  </Route>
                </Routes>
              </BrowserRouter>
            </SignalRProvider>
          </ProjectProvider>
        </AuthProvider>
      </AppThemeProvider>
    </QueryClientProvider>
  );
};

export default App;
