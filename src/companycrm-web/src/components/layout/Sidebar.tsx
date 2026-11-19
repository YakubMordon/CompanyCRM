import React from 'react';
import { NavLink, useLocation } from 'react-router-dom';
import Drawer from '@mui/material/Drawer';
import List from '@mui/material/List';
import ListItem from '@mui/material/ListItem';
import ListItemButton from '@mui/material/ListItemButton';
import ListItemIcon from '@mui/material/ListItemIcon';
import ListItemText from '@mui/material/ListItemText';
import Box from '@mui/material/Box';
import Typography from '@mui/material/Typography';
import Chip from '@mui/material/Chip';
import DashboardIcon from '@mui/icons-material/Dashboard';
import RateReviewIcon from '@mui/icons-material/RateReview';
import SyncAltIcon from '@mui/icons-material/SyncAlt';
import PeopleIcon from '@mui/icons-material/People';
import AccountCircleIcon from '@mui/icons-material/AccountCircle';
import EventNoteIcon from '@mui/icons-material/EventNote';
import AssessmentIcon from '@mui/icons-material/Assessment';
import LockIcon from '@mui/icons-material/Lock';
import { useAuth } from '../../contexts/AuthContext';

export const SIDEBAR_WIDTH = 250;

interface NavItem {
  label: string;
  path: string;
  icon: React.ReactElement;
  requiresHigherMgmt?: boolean;
}

const NAV_ITEMS: NavItem[] = [
  { label: 'Workspace', path: '/workspace', icon: <DashboardIcon /> },
  { label: 'Review Queue', path: '/reviews', icon: <RateReviewIcon /> },
  { label: 'External Sync', path: '/sync', icon: <SyncAltIcon /> },
  { label: 'Employees Directory', path: '/employees', icon: <PeopleIcon /> },
  { label: 'My Profile', path: '/profile', icon: <AccountCircleIcon /> },
  { label: 'Planning', path: '/planning', icon: <EventNoteIcon /> },
  {
    label: 'Management Portal',
    path: '/management',
    icon: <AssessmentIcon />,
    requiresHigherMgmt: true,
  },
];

interface SidebarProps {
  open: boolean;
  onClose: () => void;
}

export const Sidebar: React.FC<SidebarProps> = ({ open, onClose }) => {
  const location = useLocation();
  const { role } = useAuth();
  const isHigherMgmt = role === 'HigherManagement';

  const drawerContent = (
    <Box sx={{ display: 'flex', flexDirection: 'column', height: '100%' }}>
      <Box sx={{ p: 2.5, display: 'flex', alignItems: 'center', gap: 1 }}>
        <Box
          sx={{
            width: 36,
            height: 36,
            borderRadius: 2,
            bgcolor: 'primary.main',
            color: 'primary.contrastText',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            fontWeight: 800,
            fontSize: '1.2rem',
          }}
        >
          C
        </Box>
        <Box>
          <Typography variant="subtitle1" sx={{ fontWeight: 700, lineHeight: 1.1 }}>
            CompanyCRM
          </Typography>
          <Typography variant="caption" color="text.secondary">
            Enterprise Hub
          </Typography>
        </Box>
      </Box>

      <List sx={{ px: 1.5, flexGrow: 1 }}>
        {NAV_ITEMS.map((item) => {
          const isActive =
            location.pathname === item.path ||
            (item.path === '/workspace' && location.pathname === '/');

          return (
            <ListItem key={item.path} disablePadding sx={{ mb: 0.5 }}>
              <ListItemButton
                component={NavLink}
                to={item.path}
                onClick={onClose}
                selected={isActive}
                sx={{
                  borderRadius: 2,
                  py: 1,
                  px: 1.5,
                  '&.active, &.Mui-selected': {
                    bgcolor: 'primary.main',
                    color: 'primary.contrastText',
                    '& .MuiListItemIcon-root': {
                      color: 'primary.contrastText',
                    },
                    '&:hover': {
                      bgcolor: 'primary.dark',
                    },
                  },
                }}
              >
                <ListItemIcon
                  sx={{
                    minWidth: 38,
                    color: isActive ? 'inherit' : 'text.secondary',
                  }}
                >
                  {item.icon}
                </ListItemIcon>
                <ListItemText
                  primary={
                    <Typography variant="body2" sx={{ fontWeight: isActive ? 600 : 500 }}>
                      {item.label}
                    </Typography>
                  }
                />
                {item.requiresHigherMgmt && !isHigherMgmt && (
                  <Chip
                    icon={<LockIcon sx={{ fontSize: '12px !important' }} />}
                    label="403"
                    size="small"
                    color="default"
                    variant="outlined"
                    sx={{ height: 20, fontSize: '0.65rem' }}
                  />
                )}
              </ListItemButton>
            </ListItem>
          );
        })}
      </List>

      <Box sx={{ p: 2, borderTop: 1, borderColor: 'divider', bgcolor: 'background.default' }}>
        <Typography variant="caption" color="text.secondary" display="block">
          Active Role: <strong>{role}</strong>
        </Typography>
        <Typography variant="caption" color="text.secondary" display="block">
          .NET Aspire Orchestrated
        </Typography>
      </Box>
    </Box>
  );

  return (
    <>
      {/* Mobile drawer */}
      <Drawer
        variant="temporary"
        open={open}
        onClose={onClose}
        ModalProps={{ keepMounted: true }}
        sx={{
          display: { xs: 'block', md: 'none' },
          '& .MuiDrawer-paper': { boxSizing: 'border-box', width: SIDEBAR_WIDTH },
        }}
      >
        {drawerContent}
      </Drawer>

      {/* Desktop permanent drawer */}
      <Drawer
        variant="permanent"
        sx={{
          display: { xs: 'none', md: 'block' },
          '& .MuiDrawer-paper': {
            boxSizing: 'border-box',
            width: SIDEBAR_WIDTH,
            borderRight: 1,
            borderColor: 'divider',
          },
        }}
        open
      >
        {drawerContent}
      </Drawer>
    </>
  );
};
