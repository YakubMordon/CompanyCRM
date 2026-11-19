import React from 'react';
import Chip from '@mui/material/Chip';
import Tooltip from '@mui/material/Tooltip';
import Box from '@mui/material/Box';
import { useSignalR } from '../../contexts/SignalRContext';
import WifiIcon from '@mui/icons-material/Wifi';
import WifiOffIcon from '@mui/icons-material/WifiOff';
import SyncIcon from '@mui/icons-material/Sync';

export const SignalRBadge: React.FC = () => {
  const { status } = useSignalR();

  let color: 'success' | 'warning' | 'error' | 'default' = 'default';
  let label = 'Offline';
  let icon = <WifiOffIcon fontSize="small" />;
  let tooltipText = 'SignalR WebSocket Disconnected';

  if (status === 'Connected') {
    color = 'success';
    label = 'Live';
    icon = <WifiIcon fontSize="small" />;
    tooltipText = 'SignalR Live: Connected to /hubs/crm';
  } else if (status === 'Connecting' || status === 'Reconnecting') {
    color = 'warning';
    label = status;
    icon = <SyncIcon fontSize="small" sx={{ animation: 'spin 2s linear infinite' }} />;
    tooltipText = `SignalR: ${status}...`;
  }

  return (
    <Tooltip title={tooltipText} arrow>
      <Box sx={{ display: 'inline-flex', alignItems: 'center' }}>
        <Chip
          icon={icon}
          label={label}
          color={color}
          size="small"
          variant="outlined"
          sx={{
            fontWeight: 600,
            textTransform: 'capitalize',
            borderRadius: '16px',
            '& .MuiChip-icon': {
              marginLeft: '6px',
            },
          }}
        />
      </Box>
    </Tooltip>
  );
};
