import React from 'react';
import { Navigate } from 'react-router-dom';
import { useAuth } from '../../contexts/AuthContext';
import Box from '@mui/material/Box';
import CircularProgress from '@mui/material/CircularProgress';

interface ProtectedRouteProps {
  children: React.ReactElement;
  path: string;
}

export const ProtectedRoute: React.FC<ProtectedRouteProps> = ({ children, path }) => {
  const { canAccessPath, isLoading } = useAuth();

  if (isLoading) {
    return (
      <Box sx={{ display: 'flex', justifyContent: 'center', alignItems: 'center', height: '60vh' }}>
        <CircularProgress />
      </Box>
    );
  }

  if (!canAccessPath(path)) {
    return <Navigate to="/workspace" replace />;
  }

  return children;
};
