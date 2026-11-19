import React, { createContext, useContext, useEffect, useState, useCallback } from 'react';
import { AppRole, CurrentUserDto, LoginRequest } from '../types';
import { authApi } from '../services/authApi';

interface AuthContextType {
  token: string | null;
  currentUser: CurrentUserDto | null;
  role: AppRole;
  isLoading: boolean;
  login: (request: LoginRequest) => Promise<void>;
  switchPersona: (targetRole: AppRole) => Promise<void>;
  logout: () => void;
}

const AuthContext = createContext<AuthContextType | undefined>(undefined);

export const AuthProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const [token, setToken] = useState<string | null>(() => localStorage.getItem('auth_token'));
  const [currentUser, setCurrentUser] = useState<CurrentUserDto | null>(null);
  const [role, setRole] = useState<AppRole>('Employee');
  const [isLoading, setIsLoading] = useState<boolean>(true);

  const switchPersona = useCallback(async (targetRole: AppRole) => {
    setIsLoading(true);
    try {
      const authRes = await authApi.switchPersona(targetRole);
      localStorage.setItem('auth_token', authRes.token);
      setToken(authRes.token);
      setRole(targetRole);
      setCurrentUser({
        id: authRes.user.id,
        fullName: authRes.user.fullName,
        email: authRes.user.email,
        avatarUrl: authRes.user.avatarUrl,
        department: authRes.user.department,
        jobTitle: authRes.user.jobTitle,
        seniorityRank: authRes.user.seniorityRank,
        role: targetRole,
        permissions: authRes.permissions,
        layoutPreferencesJson: authRes.user.layoutPreferencesJson,
      });
      window.dispatchEvent(new CustomEvent('persona:switched', { detail: { role: targetRole } }));
    } catch (err) {
      console.error('Failed to switch persona:', err);
    } finally {
      setIsLoading(false);
    }
  }, []);

  const login = useCallback(async (request: LoginRequest) => {
    setIsLoading(true);
    try {
      const authRes = await authApi.login(request);
      localStorage.setItem('auth_token', authRes.token);
      setToken(authRes.token);
      const appRole = (authRes.role as AppRole) || 'Employee';
      setRole(appRole);
      setCurrentUser({
        id: authRes.user.id,
        fullName: authRes.user.fullName,
        email: authRes.user.email,
        avatarUrl: authRes.user.avatarUrl,
        department: authRes.user.department,
        jobTitle: authRes.user.jobTitle,
        seniorityRank: authRes.user.seniorityRank,
        role: appRole,
        permissions: authRes.permissions,
        layoutPreferencesJson: authRes.user.layoutPreferencesJson,
      });
    } finally {
      setIsLoading(false);
    }
  }, []);

  const logout = useCallback(() => {
    localStorage.removeItem('auth_token');
    setToken(null);
    setCurrentUser(null);
    setRole('Employee');
  }, []);

  useEffect(() => {
    const initializeAuth = async () => {
      const savedToken = localStorage.getItem('auth_token');
      if (savedToken) {
        try {
          const user = await authApi.getCurrentUser();
          setCurrentUser(user);
          setRole(user.role);
          setToken(savedToken);
        } catch {
          // If token failed, auto-initialize with default Employee persona
          await switchPersona('Employee');
        }
      } else {
        // First visit: initialize with Employee persona Alex Rivera
        await switchPersona('Employee');
      }
      setIsLoading(false);
    };

    initializeAuth();

    const handleUnauthorized = () => {
      switchPersona('Employee');
    };

    window.addEventListener('auth:unauthorized', handleUnauthorized);
    return () => {
      window.removeEventListener('auth:unauthorized', handleUnauthorized);
    };
  }, [switchPersona]);

  return (
    <AuthContext.Provider
      value={{
        token,
        currentUser,
        role,
        isLoading,
        login,
        switchPersona,
        logout,
      }}
    >
      {children}
    </AuthContext.Provider>
  );
};

export const useAuth = (): AuthContextType => {
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error('useAuth must be used within an AuthProvider');
  }
  return context;
};
