import React, { useState, useEffect, useCallback } from 'react';
import Box from '@mui/material/Box';
import Paper from '@mui/material/Paper';
import Typography from '@mui/material/Typography';
import Button from '@mui/material/Button';
import Grid from '@mui/material/Grid';
import LinearProgress from '@mui/material/LinearProgress';
import Chip from '@mui/material/Chip';
import Alert from '@mui/material/Alert';
import CircularProgress from '@mui/material/CircularProgress';
import Dialog from '@mui/material/Dialog';
import DialogTitle from '@mui/material/DialogTitle';
import DialogContent from '@mui/material/DialogContent';
import DialogActions from '@mui/material/DialogActions';
import TextField from '@mui/material/TextField';
import FormControl from '@mui/material/FormControl';
import InputLabel from '@mui/material/InputLabel';
import Select from '@mui/material/Select';
import MenuItem from '@mui/material/MenuItem';
import FormControlLabel from '@mui/material/FormControlLabel';
import Switch from '@mui/material/Switch';
import NotificationsActiveIcon from '@mui/icons-material/NotificationsActive';
import AddIcon from '@mui/icons-material/Add';
import { useProject } from '../../contexts/ProjectContext';
import {
  retainersApi,
  RetainerStatusDto,
  RetainerHealthStatus,
} from '../../services/retainersApi';

interface RetainersTabProps {
  projectId?: string;
}

const HEALTH_COLOR: Record<RetainerHealthStatus, 'success' | 'warning' | 'error'> = {
  Healthy: 'success',
  Warning: 'warning',
  Exceeded: 'error',
};

export const RetainersTab: React.FC<RetainersTabProps> = ({ projectId }) => {
  const { projects } = useProject();
  const [retainers, setRetainers] = useState<RetainerStatusDto[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [alertResult, setAlertResult] = useState<string | null>(null);

  // Upsert dialog
  const [dialogOpen, setDialogOpen] = useState(false);
  const [formProjectId, setFormProjectId] = useState<string>('');
  const [formClientName, setFormClientName] = useState('');
  const [formHours, setFormHours] = useState(160);
  const [formBudget, setFormBudget] = useState(24000);
  const [formThreshold, setFormThreshold] = useState(80);
  const [formSlack, setFormSlack] = useState(true);
  const [formEmail, setFormEmail] = useState(true);
  const [isSaving, setIsSaving] = useState(false);

  const loadRetainers = useCallback(async () => {
    setIsLoading(true);
    setError(null);
    try {
      const data = await retainersApi.getRetainers({ projectId: projectId || undefined });
      setRetainers(data);
    } catch (err: any) {
      setError(err?.message || 'Failed to load client retainers.');
    } finally {
      setIsLoading(false);
    }
  }, [projectId]);

  useEffect(() => {
    loadRetainers();
  }, [loadRetainers]);

  const handleEvaluateAlerts = async () => {
    setError(null);
    setAlertResult(null);
    try {
      const res = await retainersApi.evaluateAlerts();
      setAlertResult(
        res.alertsTriggered > 0
          ? `Triggered ${res.alertsTriggered} budget alarm(s): ${res.triggeredMessages.join(' | ')}`
          : `Evaluated ${res.evaluatedRetainers} active retainer(s). No new budget alarms triggered.`
      );
    } catch (err: any) {
      setError(err?.message || 'Failed to evaluate budget alerts.');
    }
  };

  const handleOpenCreate = () => {
    setFormProjectId(projectId || projects[0]?.id || '');
    setFormClientName('');
    setFormHours(160);
    setFormBudget(24000);
    setFormThreshold(80);
    setFormSlack(true);
    setFormEmail(true);
    setDialogOpen(true);
  };

  const handleSave = async () => {
    if (!formProjectId || !formClientName.trim()) return;
    setIsSaving(true);
    setError(null);
    try {
      await retainersApi.upsertRetainer({
        projectId: formProjectId,
        clientName: formClientName.trim(),
        monthlyRetainerHours: formHours,
        monthlyBudgetAmount: formBudget,
        warningThresholdPercent: formThreshold,
        notifySlackTeams: formSlack,
        notifyEmail: formEmail,
      });
      setDialogOpen(false);
      await loadRetainers();
    } catch (err: any) {
      setError(err?.message || 'Failed to save retainer.');
    } finally {
      setIsSaving(false);
    }
  };

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2.5 }}>
      <Paper sx={{ p: 2, display: 'flex', flexWrap: 'wrap', alignItems: 'center', justifyContent: 'space-between', gap: 2 }}>
        <Box>
          <Typography variant="h6" sx={{ fontWeight: 700 }}>
            Client Retainer Tracking & Budget Alarms
          </Typography>
          <Typography variant="body2" color="text.secondary">
            Monitor monthly billable burn against client retainer caps and trigger automated Slack, Teams, and email overage alarms.
          </Typography>
        </Box>
        <Box sx={{ display: 'flex', gap: 1.5 }}>
          <Button
            variant="outlined"
            color="warning"
            startIcon={<NotificationsActiveIcon />}
            onClick={handleEvaluateAlerts}
          >
            Run Budget Alert Check
          </Button>
          <Button variant="contained" startIcon={<AddIcon />} onClick={handleOpenCreate}>
            Configure Retainer
          </Button>
        </Box>
      </Paper>

      {error && <Alert severity="error">{error}</Alert>}
      {alertResult && <Alert severity="info">{alertResult}</Alert>}

      {isLoading ? (
        <Box sx={{ display: 'flex', justifyContent: 'center', p: 4 }}>
          <CircularProgress />
        </Box>
      ) : retainers.length === 0 ? (
        <Paper sx={{ p: 4, textAlign: 'center' }}>
          <Typography color="text.secondary">No client retainers configured for this scope.</Typography>
        </Paper>
      ) : (
        <Grid container spacing={2.5}>
          {retainers.map((r) => (
            <Grid item xs={12} md={6} key={r.id}>
              <Paper sx={{ p: 2.5, display: 'flex', flexDirection: 'column', gap: 1.5 }}>
                <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start' }}>
                  <Box>
                    <Typography variant="subtitle1" sx={{ fontWeight: 700 }}>
                      {r.clientName}
                    </Typography>
                    <Typography variant="caption" color="text.secondary">
                      {r.projectName} ({r.projectCode}) • {r.year}-{String(r.month).padStart(2, '0')}
                    </Typography>
                  </Box>
                  <Chip
                    label={r.healthStatus}
                    color={HEALTH_COLOR[r.healthStatus]}
                    size="small"
                    sx={{ fontWeight: 600 }}
                  />
                </Box>

                <Box>
                  <Box sx={{ display: 'flex', justifyContent: 'space-between', mb: 0.5 }}>
                    <Typography variant="body2">
                      Hours: {r.loggedBillableHours.toFixed(1)} / {r.monthlyRetainerHours.toFixed(1)}h
                    </Typography>
                    <Typography variant="body2" sx={{ fontWeight: 600 }}>
                      {r.hoursUtilizationPercent.toFixed(1)}%
                    </Typography>
                  </Box>
                  <LinearProgress
                    variant="determinate"
                    value={Math.min(100, r.hoursUtilizationPercent)}
                    color={HEALTH_COLOR[r.healthStatus]}
                    sx={{ height: 8, borderRadius: 4 }}
                  />
                </Box>

                <Box>
                  <Box sx={{ display: 'flex', justifyContent: 'space-between', mb: 0.5 }}>
                    <Typography variant="body2">
                      Budget: ${r.loggedBillableAmount.toLocaleString()} / ${r.monthlyBudgetAmount.toLocaleString()} {r.currency}
                    </Typography>
                    <Typography variant="body2" sx={{ fontWeight: 600 }}>
                      {r.budgetUtilizationPercent.toFixed(1)}%
                    </Typography>
                  </Box>
                  <LinearProgress
                    variant="determinate"
                    value={Math.min(100, r.budgetUtilizationPercent)}
                    color={HEALTH_COLOR[r.healthStatus]}
                    sx={{ height: 8, borderRadius: 4 }}
                  />
                </Box>

                <Typography variant="caption" color="text.secondary">
                  Warning at {r.warningThresholdPercent}% • Remaining: {r.remainingHours.toFixed(1)}h / ${r.remainingBudget.toLocaleString()}
                </Typography>
              </Paper>
            </Grid>
          ))}
        </Grid>
      )}

      <Dialog open={dialogOpen} onClose={() => setDialogOpen(false)} maxWidth="xs" fullWidth>
        <DialogTitle>Configure Client Retainer</DialogTitle>
        <DialogContent sx={{ display: 'flex', flexDirection: 'column', gap: 2, pt: 1 }}>
          <FormControl fullWidth size="small" sx={{ mt: 1 }}>
            <InputLabel>Project</InputLabel>
            <Select
              value={formProjectId}
              label="Project"
              onChange={(e) => setFormProjectId(e.target.value)}
            >
              {projects.map((p) => (
                <MenuItem key={p.id} value={p.id}>
                  {p.name} ({p.code})
                </MenuItem>
              ))}
            </Select>
          </FormControl>
          <TextField
            label="Client Name"
            size="small"
            value={formClientName}
            onChange={(e) => setFormClientName(e.target.value)}
          />
          <TextField
            label="Monthly Retainer Hours"
            type="number"
            size="small"
            value={formHours}
            onChange={(e) => setFormHours(Number(e.target.value))}
          />
          <TextField
            label="Monthly Budget Amount (USD)"
            type="number"
            size="small"
            value={formBudget}
            onChange={(e) => setFormBudget(Number(e.target.value))}
          />
          <TextField
            label="Warning Threshold (%)"
            type="number"
            size="small"
            value={formThreshold}
            onChange={(e) => setFormThreshold(Number(e.target.value))}
          />
          <FormControlLabel
            control={<Switch checked={formSlack} onChange={(e) => setFormSlack(e.target.checked)} />}
            label="Notify Slack / Teams"
          />
          <FormControlLabel
            control={<Switch checked={formEmail} onChange={(e) => setFormEmail(e.target.checked)} />}
            label="Notify Management Email"
          />
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setDialogOpen(false)}>Cancel</Button>
          <Button variant="contained" onClick={handleSave} disabled={isSaving || !formClientName.trim()}>
            {isSaving ? 'Saving...' : 'Save Retainer'}
          </Button>
        </DialogActions>
      </Dialog>
    </Box>
  );
};
