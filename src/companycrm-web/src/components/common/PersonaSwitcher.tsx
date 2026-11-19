import React, { useState } from 'react';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Menu from '@mui/material/Menu';
import MenuItem from '@mui/material/MenuItem';
import Avatar from '@mui/material/Avatar';
import Typography from '@mui/material/Typography';
import Chip from '@mui/material/Chip';
import ListItemIcon from '@mui/material/ListItemIcon';
import ListItemText from '@mui/material/ListItemText';
import ArrowDropDownIcon from '@mui/icons-material/ArrowDropDown';
import CheckIcon from '@mui/icons-material/Check';
import Snackbar from '@mui/material/Snackbar';
import Alert from '@mui/material/Alert';
import { useAuth } from '../../contexts/AuthContext';
import { AppRole } from '../../types';

interface PersonaOption {
  role: AppRole;
  name: string;
  title: string;
  email: string;
  avatarColor: string;
}

const PERSONAS: PersonaOption[] = [
  {
    role: 'Employee',
    name: 'Alex Rivera',
    title: 'Senior Full-Stack Engineer',
    email: 'alex.rivera@companycrm.local',
    avatarColor: '#1976d2',
  },
  {
    role: 'TeamLead',
    name: 'Sarah Jenkins',
    title: 'Staff Lead Engineer / Reviewer',
    email: 'sarah.jenkins@companycrm.local',
    avatarColor: '#ed6c02',
  },
  {
    role: 'HigherManagement',
    name: 'Eleanor Vance',
    title: 'Chief Operating Officer',
    email: 'eleanor.vance@companycrm.local',
    avatarColor: '#2e7d32',
  },
];

export const PersonaSwitcher: React.FC = () => {
  const { role, currentUser, switchPersona, isLoading } = useAuth();
  const [anchorEl, setAnchorEl] = useState<null | HTMLElement>(null);
  const [toastMessage, setToastMessage] = useState<string | null>(null);

  const handleClick = (event: React.MouseEvent<HTMLButtonElement>) => {
    setAnchorEl(event.currentTarget);
  };

  const handleClose = () => {
    setAnchorEl(null);
  };

  const handleSelectPersona = async (targetRole: AppRole) => {
    handleClose();
    if (targetRole === role) return;

    await switchPersona(targetRole);
    const persona = PERSONAS.find((p) => p.role === targetRole);
    setToastMessage(`Switched persona to ${persona?.name} (${targetRole})`);
  };

  const currentPersona = PERSONAS.find((p) => p.role === role) || PERSONAS[0];

  return (
    <>
      <Button
        onClick={handleClick}
        variant="outlined"
        color="inherit"
        disabled={isLoading}
        endIcon={<ArrowDropDownIcon />}
        sx={{
          textTransform: 'none',
          py: 0.5,
          px: 1.5,
          borderColor: 'divider',
          borderRadius: 2,
          display: 'flex',
          alignItems: 'center',
          gap: 1,
        }}
      >
        <Avatar
          sx={{
            width: 28,
            height: 28,
            bgcolor: currentPersona.avatarColor,
            fontSize: 13,
            fontWeight: 600,
          }}
        >
          {currentPersona.name.charAt(0)}
        </Avatar>
        <Box sx={{ textAlign: 'left', display: { xs: 'none', sm: 'block' } }}>
          <Typography variant="body2" sx={{ fontWeight: 600, lineHeight: 1.2 }}>
            {currentUser?.fullName || currentPersona.name}
          </Typography>
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.5 }}>
            <Typography variant="caption" color="text.secondary">
              {currentPersona.title}
            </Typography>
          </Box>
        </Box>
        <Chip
          label={role}
          size="small"
          color={
            role === 'HigherManagement'
              ? 'success'
              : role === 'TeamLead'
              ? 'warning'
              : 'primary'
          }
          sx={{ height: 20, fontSize: '0.7rem', fontWeight: 600 }}
        />
      </Button>

      <Menu
        anchorEl={anchorEl}
        open={Boolean(anchorEl)}
        onClose={handleClose}
        PaperProps={{
          sx: { minWidth: 280, borderRadius: 2, mt: 1 },
        }}
      >
        <Typography
          variant="caption"
          sx={{ px: 2, py: 1, display: 'block', color: 'text.secondary', fontWeight: 600 }}
        >
          QUICK PERSONA SWITCHER
        </Typography>
        {PERSONAS.map((p) => {
          const isSelected = p.role === role;
          return (
            <MenuItem
              key={p.role}
              onClick={() => handleSelectPersona(p.role)}
              selected={isSelected}
              sx={{ py: 1, px: 2 }}
            >
              <ListItemIcon>
                <Avatar
                  sx={{
                    width: 32,
                    height: 32,
                    bgcolor: p.avatarColor,
                    fontSize: 14,
                    fontWeight: 600,
                  }}
                >
                  {p.name.charAt(0)}
                </Avatar>
              </ListItemIcon>
              <ListItemText
                primary={
                  <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
                    <Typography variant="body2" sx={{ fontWeight: 600 }}>
                      {p.name}
                    </Typography>
                    <Chip
                      label={p.role}
                      size="small"
                      color={
                        p.role === 'HigherManagement'
                          ? 'success'
                          : p.role === 'TeamLead'
                          ? 'warning'
                          : 'primary'
                      }
                      sx={{ height: 18, fontSize: '0.65rem' }}
                    />
                  </Box>
                }
                secondary={p.title}
              />
              {isSelected && <CheckIcon fontSize="small" color="primary" sx={{ ml: 1 }} />}
            </MenuItem>
          );
        })}
      </Menu>

      <Snackbar
        open={Boolean(toastMessage)}
        autoHideDuration={3000}
        onClose={() => setToastMessage(null)}
        anchorOrigin={{ vertical: 'bottom', horizontal: 'right' }}
      >
        <Alert
          onClose={() => setToastMessage(null)}
          severity="success"
          sx={{ width: '100%', fontWeight: 500 }}
        >
          {toastMessage}
        </Alert>
      </Snackbar>
    </>
  );
};
