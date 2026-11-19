import React, { useState } from 'react';
import { Outlet } from 'react-router-dom';
import Box from '@mui/material/Box';
import Toolbar from '@mui/material/Toolbar';
import { Header } from './Header';
import { Sidebar, SIDEBAR_WIDTH } from './Sidebar';

export const AppShell: React.FC = () => {
  const [mobileOpen, setMobileOpen] = useState(false);

  const handleToggleSidebar = () => {
    setMobileOpen((prev) => !prev);
  };

  return (
    <Box sx={{ display: 'flex', minHeight: '100vh', bgcolor: 'background.default' }}>
      <Sidebar open={mobileOpen} onClose={() => setMobileOpen(false)} />

      <Box
        sx={{
          flexGrow: 1,
          display: 'flex',
          flexDirection: 'column',
          width: { md: `calc(100% - ${SIDEBAR_WIDTH}px)` },
          minWidth: 0,
        }}
      >
        <Header onToggleSidebar={handleToggleSidebar} />

        <Box component="main" sx={{ flexGrow: 1, p: { xs: 2, sm: 3 }, minWidth: 0 }}>
          <Outlet />
        </Box>
      </Box>
    </Box>
  );
};
