import React, { createContext, useContext, useEffect, useState, useMemo } from 'react';
import { ThemeProvider as MuiThemeProvider } from '@mui/material/styles';
import CssBaseline from '@mui/material/CssBaseline';
import { createAppTheme } from '../theme/theme';
import { ThemeMode, getNextThemeMode, resolveSavedThemeMode } from './contextHelpers';

interface ThemeContextType {
  mode: ThemeMode;
  toggleTheme: () => void;
  setThemeMode: (mode: ThemeMode) => void;
}

const ThemeContext = createContext<ThemeContextType | undefined>(undefined);

export const AppThemeProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const [mode, setMode] = useState<ThemeMode>(() =>
    resolveSavedThemeMode(localStorage.getItem('crm_theme_mode'))
  );

  const toggleTheme = () => {
    setMode((prev) => {
      const next = getNextThemeMode(prev);
      localStorage.setItem('crm_theme_mode', next);
      return next;
    });
  };

  const setThemeMode = (newMode: 'light' | 'dark') => {
    setMode(newMode);
    localStorage.setItem('crm_theme_mode', newMode);
  };

  const theme = useMemo(() => createAppTheme(mode), [mode]);

  useEffect(() => {
    localStorage.setItem('crm_theme_mode', mode);
  }, [mode]);

  return (
    <ThemeContext.Provider value={{ mode, toggleTheme, setThemeMode }}>
      <MuiThemeProvider theme={theme}>
        <CssBaseline />
        {children}
      </MuiThemeProvider>
    </ThemeContext.Provider>
  );
};

export const useAppTheme = (): ThemeContextType => {
  const context = useContext(ThemeContext);
  if (!context) {
    throw new Error('useAppTheme must be used within an AppThemeProvider');
  }
  return context;
};
