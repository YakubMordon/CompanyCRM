import React, { useState } from 'react';
import Paper from '@mui/material/Paper';
import Box from '@mui/material/Box';
import Typography from '@mui/material/Typography';
import Button from '@mui/material/Button';
import Chip from '@mui/material/Chip';
import CircularProgress from '@mui/material/CircularProgress';
import SyncIcon from '@mui/icons-material/Sync';
import CheckCircleIcon from '@mui/icons-material/CheckCircle';
import ErrorOutlineIcon from '@mui/icons-material/ErrorOutline';
import WarningAmberIcon from '@mui/icons-material/WarningAmber';
import InfoOutlinedIcon from '@mui/icons-material/InfoOutlined';
import Snackbar from '@mui/material/Snackbar';
import Alert from '@mui/material/Alert';
import { useSignalR } from '../../contexts/SignalRContext';
import { syncApi } from '../../services/syncApi';
import { formatReconciliationSummary, getSyncChipColor } from './syncHelpers';

interface SyncLogFeedProps {
  projectId: string;
  onReconciliationDone?: () => void;
}

export const SyncLogFeed: React.FC<SyncLogFeedProps> = ({ projectId, onReconciliationDone }) => {
  const { syncEvents } = useSignalR();
  const [isRunningReconciliation, setIsRunningReconciliation] = useState(false);
  const [feedback, setFeedback] = useState<{ severity: 'success' | 'error'; message: string } | null>(
    null
  );

  const handleRunReconciliation = async () => {
    setIsRunningReconciliation(true);
    setFeedback(null);

    try {
      const res = await syncApi.reconcileProject(projectId);
      setFeedback(formatReconciliationSummary(res));
      if (onReconciliationDone) {
        onReconciliationDone();
      }
    } catch (err: any) {
      setFeedback({
        severity: 'error',
        message: err?.response?.data?.detail || err?.message || 'Reconciliation failed.',
      });
    } finally {
      setIsRunningReconciliation(false);
    }
  };

  const getEventIcon = (status: string) => {
    switch (status) {
      case 'WorkItemPushed':
      case 'WorkItemImported':
      case 'SyncCompleted':
        return <CheckCircleIcon fontSize="small" color="success" />;
      case 'ConflictDetected':
        return <WarningAmberIcon fontSize="small" color="warning" />;
      case 'SyncFailed':
        return <ErrorOutlineIcon fontSize="small" color="error" />;
      default:
        return <InfoOutlinedIcon fontSize="small" color="info" />;
    }
  };

  const getChipColor = getSyncChipColor;

  return (
    <Paper variant="outlined" sx={{ p: 3, borderRadius: 2 }}>
      <Box
        sx={{
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          flexWrap: 'wrap',
          gap: 2,
          mb: 2,
        }}
      >
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
          <SyncIcon color="primary" />
          <Typography variant="h6" sx={{ fontWeight: 700 }}>
            Sync Operations & Real-Time SignalR Feed
          </Typography>
        </Box>

        <Button
          variant="contained"
          color="primary"
          startIcon={
            isRunningReconciliation ? (
              <CircularProgress size={16} color="inherit" />
            ) : (
              <SyncIcon />
            )
          }
          onClick={handleRunReconciliation}
          disabled={isRunningReconciliation}
          sx={{ fontWeight: 600 }}
        >
          {isRunningReconciliation ? 'Reconciling...' : 'Run Sync Reconciliation Now'}
        </Button>
      </Box>

      <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mb: 2 }}>
        Outbound changes are automatically hashed with SHA-256 to prevent infinite echo loops.
        Remote webhook updates and scheduled 15-minute background jobs appear in this live stream.
      </Typography>

      <Box
        sx={{
          maxHeight: 280,
          overflowY: 'auto',
          border: 1,
          borderColor: 'divider',
          borderRadius: 2,
          p: 1.5,
          bgcolor: 'background.default',
        }}
      >
        {syncEvents.length === 0 ? (
          <Box sx={{ py: 4, textAlign: 'center' }}>
            <Typography variant="body2" color="text.secondary">
              No recent sync events captured on this connection. Run reconciliation or trigger an update to see live events.
            </Typography>
          </Box>
        ) : (
          <Box sx={{ display: 'flex', flexDirection: 'column', gap: 1 }}>
            {syncEvents.map((evt, idx) => {
              const status = evt.status || evt.eventType || 'SyncEvent';
              return (
                <Box
                  key={idx}
                  sx={{
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'space-between',
                    p: 1,
                    bgcolor: 'background.paper',
                    borderRadius: 1.5,
                    border: 1,
                    borderColor: 'divider',
                  }}
                >
                  <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
                    {getEventIcon(status)}
                    <Chip
                      label={status}
                      size="small"
                      color={getChipColor(status)}
                      variant="outlined"
                      sx={{ height: 20, fontSize: '0.65rem', fontWeight: 600 }}
                    />
                    {evt.externalId && (
                      <Chip
                        label={evt.externalId}
                        size="small"
                        color="secondary"
                        sx={{ height: 20, fontSize: '0.65rem' }}
                      />
                    )}
                    <Typography variant="body2" sx={{ fontWeight: 500 }}>
                      {evt.message}
                    </Typography>
                  </Box>
                  <Typography variant="caption" color="text.secondary">
                    {evt.timestamp ? new Date(evt.timestamp).toLocaleTimeString() : new Date().toLocaleTimeString()}
                  </Typography>
                </Box>
              );
            })}
          </Box>
        )}
      </Box>

      <Snackbar
        open={Boolean(feedback)}
        autoHideDuration={4000}
        onClose={() => setFeedback(null)}
        anchorOrigin={{ vertical: 'bottom', horizontal: 'center' }}
      >
        {feedback ? (
          <Alert
            onClose={() => setFeedback(null)}
            severity={feedback.severity}
            sx={{ width: '100%', fontWeight: 500 }}
          >
            {feedback.message}
          </Alert>
        ) : undefined}
      </Snackbar>
    </Paper>
  );
};
