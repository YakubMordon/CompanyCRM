import React, { useState, useEffect } from 'react';
import Dialog from '@mui/material/Dialog';
import DialogTitle from '@mui/material/DialogTitle';
import DialogContent from '@mui/material/DialogContent';
import DialogActions from '@mui/material/DialogActions';
import Button from '@mui/material/Button';
import TextField from '@mui/material/TextField';
import FormControlLabel from '@mui/material/FormControlLabel';
import Switch from '@mui/material/Switch';
import Checkbox from '@mui/material/Checkbox';
import Box from '@mui/material/Box';
import Typography from '@mui/material/Typography';
import Alert from '@mui/material/Alert';
import CircularProgress from '@mui/material/CircularProgress';
import Divider from '@mui/material/Divider';
import ToggleButton from '@mui/material/ToggleButton';
import ToggleButtonGroup from '@mui/material/ToggleButtonGroup';
import PlayArrowIcon from '@mui/icons-material/PlayArrow';
import AccessTimeIcon from '@mui/icons-material/AccessTime';
import OpenInNewIcon from '@mui/icons-material/OpenInNew';
import { CutoffExecutionSummaryDto, PlanningCutoffConfigDto } from '../../types';
import { planningApi } from '../../services/planningApi';
import {
  CutoffScopeMode,
  DAYS_OF_WEEK,
  DEFAULT_EXCLUDED_DAY_INDICES,
  buildUpdateCutoffConfigDto,
  isInheritedGlobalFallback,
  parseExcludedDaysToIndices,
  resolveTargetCutoffProjectId,
} from './cutoffHelpers';

interface CutoffConfigDialogProps {
  open: boolean;
  onClose: () => void;
  projectId?: string;
  projectName?: string;
}

export const CutoffConfigDialog: React.FC<CutoffConfigDialogProps> = ({
  open,
  onClose,
  projectId,
  projectName,
}) => {
  const [scopeMode, setScopeMode] = useState<CutoffScopeMode>('global');
  const [config, setConfig] = useState<PlanningCutoffConfigDto | null>(null);
  const [cutoffTime, setCutoffTime] = useState('10:00:00');
  const [timeZoneId, setTimeZoneId] = useState('UTC');
  const [isReminderEmailEnabled, setIsReminderEmailEnabled] = useState(true);
  const [excludedDays, setExcludedDays] = useState<number[]>(DEFAULT_EXCLUDED_DAY_INDICES);

  const [isLoading, setIsLoading] = useState(true);
  const [isSaving, setIsSaving] = useState(false);
  const [isRunningCheck, setIsRunningCheck] = useState(false);
  const [checkSummary, setCheckSummary] = useState<CutoffExecutionSummaryDto | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState<string | null>(null);

  const targetProjectId = resolveTargetCutoffProjectId(scopeMode, projectId);

  useEffect(() => {
    if (!open || !projectId) {
      setScopeMode('global');
    }
  }, [open, projectId]);

  useEffect(() => {
    if (open) {
      setIsLoading(true);
      setError(null);
      setSuccess(null);
      setCheckSummary(null);

      planningApi
        .getCutoffConfig(targetProjectId ?? undefined)
        .then((cfg) => {
          setConfig(cfg);
          setCutoffTime(cfg.cutoffTimeLocal || '10:00:00');
          setTimeZoneId(cfg.timeZoneId || 'UTC');
          setIsReminderEmailEnabled(cfg.isReminderEnabled ?? cfg.isReminderEmailEnabled ?? true);
          setExcludedDays(parseExcludedDaysToIndices(cfg));
        })
        .catch((err: any) => {
          console.error('Failed to load cutoff config:', err);
          setError(
            err?.response?.data?.message ||
              err?.response?.data?.detail ||
              err?.message ||
              'Failed to load cutoff configuration.'
          );
        })
        .finally(() => setIsLoading(false));
    }
  }, [open, targetProjectId]);

  const handleToggleDay = (dayIndex: number) => {
    setExcludedDays((prev) =>
      prev.includes(dayIndex) ? prev.filter((d) => d !== dayIndex) : [...prev, dayIndex]
    );
  };

  const handleSave = async (e: React.FormEvent) => {
    e.preventDefault();
    setIsSaving(true);
    setError(null);
    setSuccess(null);

    try {
      const effectiveProjectId = resolveTargetCutoffProjectId(scopeMode, projectId);
      const dto = buildUpdateCutoffConfigDto({
        projectId: effectiveProjectId,
        cutoffTimeLocal: cutoffTime,
        timeZoneId,
        isReminderEnabled: isReminderEmailEnabled,
        excludedDayIndices: excludedDays,
      });

      const updated = await planningApi.updateCutoffConfig(dto);
      setConfig(updated);
      setExcludedDays(parseExcludedDaysToIndices(updated));
      setSuccess(
        effectiveProjectId
          ? 'Project cutoff override updated successfully.'
          : 'Global cutoff configuration updated successfully.'
      );
    } catch (err: any) {
      setError(
        err?.response?.data?.message ||
          err?.response?.data?.detail ||
          err?.message ||
          'Failed to update configuration.'
      );
    } finally {
      setIsSaving(false);
    }
  };

  const handleRunCutoffCheckNow = async () => {
    setIsRunningCheck(true);
    setError(null);

    try {
      const summary = await planningApi.runCutoffCheckNow(targetProjectId);
      setCheckSummary(summary);
    } catch (err: any) {
      setError(
        err?.response?.data?.message ||
          err?.response?.data?.detail ||
          err?.message ||
          'Failed to trigger cutoff check job.'
      );
    } finally {
      setIsRunningCheck(false);
    }
  };

  const scopeHelperText =
    scopeMode === 'global'
      ? 'Editing Organization Default (Global). Applies to the scheduled Hangfire reminder job and all projects without a custom override.'
      : isInheritedGlobalFallback(scopeMode, config?.projectId)
        ? 'No custom override exists for this project yet — showing Global Default values. Saving will create a Project Override.'
        : `Editing custom Project Override for ${config?.projectName || projectName || 'selected project'}.`;

  return (
    <Dialog open={open} onClose={onClose} maxWidth="sm" fullWidth>
      <form onSubmit={handleSave}>
        <DialogTitle sx={{ display: 'flex', alignItems: 'center', gap: 1, fontWeight: 700 }}>
          <AccessTimeIcon color="primary" />
          Planning Cutoff & Automated Reminders
        </DialogTitle>

        <DialogContent dividers>
          {isLoading ? (
            <Box sx={{ display: 'flex', justifyContent: 'center', p: 4 }}>
              <CircularProgress size={28} />
            </Box>
          ) : (
            <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2.5 }}>
              {error && <Alert severity="error">{error}</Alert>}
              {success && <Alert severity="success">{success}</Alert>}

              <Box>
                <Typography variant="subtitle2" sx={{ fontWeight: 600, mb: 1 }}>
                  Configuration Scope
                </Typography>
                <ToggleButtonGroup
                  value={scopeMode}
                  exclusive
                  size="small"
                  fullWidth
                  onChange={(_e, nextScope: CutoffScopeMode | null) => {
                    if (nextScope) {
                      setScopeMode(nextScope);
                    }
                  }}
                >
                  <ToggleButton value="global" sx={{ fontWeight: 600 }}>
                    Organization Default (Global)
                  </ToggleButton>
                  <ToggleButton value="project" disabled={!projectId} sx={{ fontWeight: 600 }}>
                    Project Override ({projectName || 'Active Project'})
                  </ToggleButton>
                </ToggleButtonGroup>
                <Alert severity="info" sx={{ mt: 1.5 }}>
                  {scopeHelperText}
                </Alert>
              </Box>

              <Typography variant="body2" color="text.secondary">
                Configure the daily planning submission deadline. The automated Hangfire background job
                scans for missing plans after this cutoff time and delivers reminder emails via MailKit.
              </Typography>

              <Box sx={{ display: 'flex', gap: 2 }}>
                <TextField
                  size="small"
                  label="Cutoff Time Local"
                  placeholder="10:00:00"
                  value={cutoffTime}
                  onChange={(e) => setCutoffTime(e.target.value)}
                  fullWidth
                  required
                />

                <TextField
                  size="small"
                  label="Time Zone Identifier"
                  placeholder="UTC or America/New_York"
                  value={timeZoneId}
                  onChange={(e) => setTimeZoneId(e.target.value)}
                  fullWidth
                  required
                />
              </Box>

              <FormControlLabel
                control={
                  <Switch
                    checked={isReminderEmailEnabled}
                    onChange={(e) => setIsReminderEmailEnabled(e.target.checked)}
                    color="primary"
                  />
                }
                label={<Typography variant="body2">Send Automated Reminder Emails</Typography>}
              />

              <Box>
                <Typography variant="subtitle2" sx={{ fontWeight: 600, mb: 1 }}>
                  Excluded Days of Week (No Reminders)
                </Typography>
                <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 1 }}>
                  {DAYS_OF_WEEK.map((day) => (
                    <FormControlLabel
                      key={day.index}
                      control={
                        <Checkbox
                          size="small"
                          checked={excludedDays.includes(day.index)}
                          onChange={() => handleToggleDay(day.index)}
                        />
                      }
                      label={<Typography variant="body2">{day.label}</Typography>}
                    />
                  ))}
                </Box>
              </Box>

              <Divider sx={{ my: 1 }} />

              {/* Run Cutoff Check Now Test Button */}
              <Box sx={{ p: 2, bgcolor: 'action.hover', borderRadius: 2 }}>
                <Box
                  sx={{
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'space-between',
                    flexWrap: 'wrap',
                    gap: 1.5,
                  }}
                >
                  <Box>
                    <Typography variant="subtitle2" sx={{ fontWeight: 700 }}>
                      On-Demand Cutoff Check Trigger
                    </Typography>
                    <Typography variant="caption" color="text.secondary">
                      Immediately execute the cutoff evaluation job and dispatch notification emails.
                    </Typography>
                  </Box>

                  <Button
                    variant="outlined"
                    color="primary"
                    startIcon={
                      isRunningCheck ? <CircularProgress size={16} color="inherit" /> : <PlayArrowIcon />
                    }
                    onClick={handleRunCutoffCheckNow}
                    disabled={isRunningCheck}
                    sx={{ fontWeight: 600 }}
                  >
                    {isRunningCheck ? 'Checking...' : 'Run Cutoff Check Now'}
                  </Button>
                </Box>

                {checkSummary && (
                  <Box sx={{ mt: 2, p: 1.5, bgcolor: 'background.paper', borderRadius: 1.5, border: 1, borderColor: 'divider' }}>
                    <Typography variant="subtitle2" sx={{ fontWeight: 600, color: 'success.main' }}>
                      Execution Result: {checkSummary.status}
                    </Typography>
                    <Typography variant="body2" sx={{ mt: 0.5 }}>
                      Reminders Sent:{' '}
                      <strong>{checkSummary.remindersSent ?? checkSummary.reminderEmailsSentCount ?? 0}</strong>
                      {checkSummary.executionDurationMs !== undefined && (
                        <span> &bull; Duration: <strong>{checkSummary.executionDurationMs}ms</strong></span>
                      )}
                    </Typography>
                    {((checkSummary.notifiedEmployeeEmails && checkSummary.notifiedEmployeeEmails.length > 0) ||
                      (checkSummary.missingPlanEmployeeNames && checkSummary.missingPlanEmployeeNames.length > 0)) && (
                      <Typography variant="caption" color="warning.main" display="block" sx={{ mt: 0.5 }}>
                        Employees notified: {(checkSummary.notifiedEmployeeEmails || checkSummary.missingPlanEmployeeNames || []).join(', ')}
                      </Typography>
                    )}
                    <Button
                      size="small"
                      startIcon={<OpenInNewIcon />}
                      href="http://localhost:8025"
                      target="_blank"
                      sx={{ mt: 1, textTransform: 'none' }}
                    >
                      Inspect Delivered Emails in MailPit (:8025)
                    </Button>
                  </Box>
                )}
              </Box>
            </Box>
          )}
        </DialogContent>

        <DialogActions sx={{ px: 3, py: 2 }}>
          <Button onClick={onClose} disabled={isSaving}>
            Close
          </Button>
          <Button type="submit" variant="contained" disabled={isSaving || isLoading}>
            {isSaving ? 'Saving...' : 'Save Configuration'}
          </Button>
        </DialogActions>
      </form>
    </Dialog>
  );
};
