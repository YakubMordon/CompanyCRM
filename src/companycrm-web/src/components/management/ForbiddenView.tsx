import React from 'react';
import Paper from '@mui/material/Paper';
import Box from '@mui/material/Box';
import Typography from '@mui/material/Typography';
import Button from '@mui/material/Button';
import LockOutlinedIcon from '@mui/icons-material/LockOutlined';
import PersonSwitchIcon from '@mui/icons-material/SwitchAccount';
import Alert from '@mui/material/Alert';
import { useAuth } from '../../contexts/AuthContext';

export const ForbiddenView: React.FC = () => {
  const { currentUser, role, switchPersona, isLoading } = useAuth();

  return (
    <Box
      sx={{
        display: 'flex',
        justifyContent: 'center',
        alignItems: 'center',
        minHeight: 'calc(100vh - 160px)',
        p: 2,
      }}
    >
      <Paper
        variant="outlined"
        sx={{
          p: { xs: 3, sm: 6 },
          maxWidth: 620,
          textAlign: 'center',
          borderRadius: 3,
          borderColor: 'error.main',
          borderWidth: 1.5,
          bgcolor: 'background.paper',
        }}
      >
        <Box
          sx={{
            width: 80,
            height: 80,
            borderRadius: '50%',
            bgcolor: 'error.lighter',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            mx: 'auto',
            mb: 2.5,
          }}
        >
          <LockOutlinedIcon sx={{ fontSize: 44, color: 'error.main' }} />
        </Box>

        <Typography variant="h4" sx={{ fontWeight: 800, mb: 1, color: 'text.primary' }}>
          403 — Forbidden: Access Restricted
        </Typography>

        <Typography variant="body1" color="text.secondary" sx={{ mb: 3 }}>
          The Management Portal contains sensitive executive compensation, internal labor cost
          rates, and financial profitability analytics. Access is strictly restricted to the{' '}
          <strong>HigherManagement</strong> role.
        </Typography>

        <Alert severity="warning" sx={{ mb: 3, textAlign: 'left' }}>
          Your current persona is <strong>{currentUser?.fullName}</strong> ({currentUser?.email}) with
          role <strong>{role}</strong>. Non-executive accounts cannot query <code>/api/management/*</code>.
        </Alert>

        <Box sx={{ p: 2.5, bgcolor: 'action.hover', borderRadius: 2 }}>
          <Typography variant="subtitle2" sx={{ fontWeight: 700, mb: 1 }}>
            Quick Persona Switch
          </Typography>
          <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
            Switch to Eleanor Vance (Chief Operating Officer) to unlock and inspect the restricted
            portal and analytics dashboards.
          </Typography>

          <Button
            variant="contained"
            color="success"
            size="large"
            startIcon={<PersonSwitchIcon />}
            onClick={() => switchPersona('HigherManagement')}
            disabled={isLoading}
            sx={{ fontWeight: 700, px: 3 }}
          >
            Switch to Eleanor Vance (HigherManagement)
          </Button>
        </Box>
      </Paper>
    </Box>
  );
};
