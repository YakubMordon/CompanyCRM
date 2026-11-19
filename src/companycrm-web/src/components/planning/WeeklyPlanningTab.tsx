import React, { useState, useEffect, useCallback } from 'react';
import Box from '@mui/material/Box';
import Typography from '@mui/material/Typography';
import Paper from '@mui/material/Paper';
import Grid from '@mui/material/Grid';
import TextField from '@mui/material/TextField';
import Button from '@mui/material/Button';
import IconButton from '@mui/material/IconButton';
import Chip from '@mui/material/Chip';
import FormControl from '@mui/material/FormControl';
import InputLabel from '@mui/material/InputLabel';
import Select from '@mui/material/Select';
import MenuItem from '@mui/material/MenuItem';
import Alert from '@mui/material/Alert';
import CircularProgress from '@mui/material/CircularProgress';
import ChevronLeftIcon from '@mui/icons-material/ChevronLeft';
import ChevronRightIcon from '@mui/icons-material/ChevronRight';
import SaveIcon from '@mui/icons-material/Save';
import EventAvailableIcon from '@mui/icons-material/EventAvailable';
import { CreateOrUpdateWeeklyPlanDto, WeeklyPlanDto, WeeklyPlanStatus } from '../../types';
import { planningApi } from '../../services/planningApi';
import { useAuth } from '../../contexts/AuthContext';
import { useProject } from '../../contexts/ProjectContext';

const formatUtcDate = (utcDate: Date): string => utcDate.toISOString().split('T')[0];

const parseIsoDateUtc = (isoDate: string): Date => {
  const [year, month, day] = isoDate.split('-').map(Number);
  return new Date(Date.UTC(year, month - 1, day));
};

// Helper: Normalize any local Date or YYYY-MM-DD string to its Monday (YYYY-MM-DD) timezone-agnostically
export const getMonday = (d: Date | string): string => {
  const utcDate =
    typeof d === 'string'
      ? parseIsoDateUtc(d)
      : new Date(Date.UTC(d.getFullYear(), d.getMonth(), d.getDate()));
  const day = utcDate.getUTCDay();
  const diff = utcDate.getUTCDate() - day + (day === 0 ? -6 : 1);
  utcDate.setUTCDate(diff);
  return formatUtcDate(utcDate);
};

export const shiftIsoDays = (isoDate: string, deltaDays: number): string => {
  const utcDate = parseIsoDateUtc(isoDate);
  utcDate.setUTCDate(utcDate.getUTCDate() + deltaDays);
  return formatUtcDate(utcDate);
};

export const WeeklyPlanningTab: React.FC = () => {
  const { currentUser } = useAuth();
  const { activeProject } = useProject();

  const [currentMonday, setCurrentMonday] = useState(() => getMonday(new Date()));
  const [plans, setPlans] = useState<WeeklyPlanDto[]>([]);
  const [isLoading, setIsLoading] = useState(true);

  // Form state
  const [goalsMarkdown, setGoalsMarkdown] = useState('');
  const [focusAreasText, setFocusAreasText] = useState('');
  const [keyDeliverables, setKeyDeliverables] = useState('');
  const [status, setStatus] = useState<WeeklyPlanStatus>('Submitted');
  const [isSaving, setIsSaving] = useState(false);
  const [feedback, setFeedback] = useState<{ severity: 'success' | 'error'; message: string } | null>(
    null
  );

  const loadPlans = useCallback(async () => {
    setIsLoading(true);
    try {
      const data = await planningApi.getWeeklyPlans({
        weekStartDate: currentMonday,
        projectId: activeProject?.id,
      });
      setPlans(data);

      // Populate user's own plan if present
      const myPlan = data.find((p) => p.employeeId === currentUser?.id);
      if (myPlan) {
        setGoalsMarkdown(myPlan.goalsMarkdown || '');
        setFocusAreasText(myPlan.focusAreasJson || '');
        setKeyDeliverables(myPlan.keyDeliverables || '');
        setStatus(myPlan.status);
      } else {
        setGoalsMarkdown('');
        setFocusAreasText('');
        setKeyDeliverables('');
        setStatus('Submitted');
      }
    } catch (err) {
      console.error('Failed to load weekly plans:', err);
    } finally {
      setIsLoading(false);
    }
  }, [currentMonday, activeProject, currentUser]);

  useEffect(() => {
    loadPlans();
  }, [loadPlans]);

  const handlePrevWeek = () => {
    setCurrentMonday((prev) => getMonday(shiftIsoDays(prev, -7)));
  };

  const handleNextWeek = () => {
    setCurrentMonday((prev) => getMonday(shiftIsoDays(prev, 7)));
  };

  const handleSave = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!goalsMarkdown.trim()) {
      setFeedback({ severity: 'error', message: 'Goals description is required.' });
      return;
    }

    setIsSaving(true);
    setFeedback(null);

    try {
      const dto: CreateOrUpdateWeeklyPlanDto = {
        weekStartDate: currentMonday,
        projectId: activeProject?.id || null,
        goalsMarkdown: goalsMarkdown.trim(),
        focusAreasJson: focusAreasText.trim() || null,
        keyDeliverables: keyDeliverables.trim() || null,
        status,
      };

      await planningApi.createOrUpdateWeeklyPlan(dto);
      setFeedback({ severity: 'success', message: 'Weekly plan saved successfully.' });
      loadPlans();
    } catch (err: any) {
      setFeedback({
        severity: 'error',
        message: err?.response?.data?.detail || err?.message || 'Failed to save weekly plan.',
      });
    } finally {
      setIsSaving(false);
    }
  };

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', gap: 3 }}>
      {/* Week Navigator Bar */}
      <Paper
        variant="outlined"
        sx={{
          p: 2,
          borderRadius: 2,
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          flexWrap: 'wrap',
          gap: 2,
        }}
      >
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
          <IconButton onClick={handlePrevWeek} size="small">
            <ChevronLeftIcon />
          </IconButton>
          <Box sx={{ textAlign: 'center' }}>
            <Typography variant="subtitle1" sx={{ fontWeight: 700 }}>
              Week starting Monday, {currentMonday}
            </Typography>
            <Typography variant="caption" color="text.secondary">
              Monday-normalized planning horizon
            </Typography>
          </Box>
          <IconButton onClick={handleNextWeek} size="small">
            <ChevronRightIcon />
          </IconButton>
        </Box>

        <Button
          size="small"
          variant="outlined"
          startIcon={<EventAvailableIcon />}
          onClick={() => setCurrentMonday(getMonday(new Date()))}
          sx={{ fontWeight: 600 }}
        >
          Current Week
        </Button>
      </Paper>

      {feedback && (
        <Alert severity={feedback.severity} onClose={() => setFeedback(null)}>
          {feedback.message}
        </Alert>
      )}

      <Grid container spacing={3}>
        {/* Left: My Weekly Goals Form */}
        <Grid item xs={12} md={6}>
          <Paper variant="outlined" sx={{ p: 3, borderRadius: 2 }}>
            <Typography variant="h6" sx={{ fontWeight: 700, mb: 2 }}>
              My Weekly Goals & Deliverables
            </Typography>

            <form onSubmit={handleSave}>
              <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
                <TextField
                  label="Core Objectives & Focus"
                  multiline
                  rows={4}
                  placeholder="Outline the primary 2-3 objectives you commit to completing this week..."
                  value={goalsMarkdown}
                  onChange={(e) => setGoalsMarkdown(e.target.value)}
                  required
                  fullWidth
                />

                <TextField
                  label="Focus Areas / Tags"
                  placeholder="e.g. Kanban, SignalR, ADO Sync..."
                  value={focusAreasText}
                  onChange={(e) => setFocusAreasText(e.target.value)}
                  fullWidth
                />

                <TextField
                  label="Key Deliverables"
                  multiline
                  rows={3}
                  placeholder="Specific PRs, tickets, or documentation outputs..."
                  value={keyDeliverables}
                  onChange={(e) => setKeyDeliverables(e.target.value)}
                  fullWidth
                />

                <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', mt: 1 }}>
                  <FormControl size="small" sx={{ width: 140 }}>
                    <InputLabel>Status</InputLabel>
                    <Select
                      value={status}
                      label="Status"
                      onChange={(e) => setStatus(e.target.value as WeeklyPlanStatus)}
                    >
                      <MenuItem value="Draft">Draft</MenuItem>
                      <MenuItem value="Submitted">Submitted</MenuItem>
                      <MenuItem value="Completed">Completed</MenuItem>
                    </Select>
                  </FormControl>

                  <Button
                    type="submit"
                    variant="contained"
                    startIcon={<SaveIcon />}
                    disabled={isSaving}
                    sx={{ fontWeight: 600 }}
                  >
                    {isSaving ? 'Saving...' : 'Save Weekly Plan'}
                  </Button>
                </Box>
              </Box>
            </form>
          </Paper>
        </Grid>

        {/* Right: Team Weekly Plans */}
        <Grid item xs={12} md={6}>
          <Paper variant="outlined" sx={{ p: 3, borderRadius: 2 }}>
            <Typography variant="h6" sx={{ fontWeight: 700, mb: 2 }}>
              Team Weekly Goals Overview ({plans.length})
            </Typography>

            {isLoading ? (
              <Box sx={{ display: 'flex', justifyContent: 'center', py: 4 }}>
                <CircularProgress size={28} />
              </Box>
            ) : plans.length === 0 ? (
              <Box sx={{ py: 4, textAlign: 'center' }}>
                <Typography variant="body2" color="text.secondary">
                  No plans submitted by team members for this week yet.
                </Typography>
              </Box>
            ) : (
              <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
                {plans.map((p) => (
                  <Paper
                    key={p.id}
                    variant="outlined"
                    sx={{ p: 2, borderRadius: 2, bgcolor: 'background.default' }}
                  >
                    <Box
                      sx={{
                        display: 'flex',
                        alignItems: 'center',
                        justifyContent: 'space-between',
                        mb: 1,
                      }}
                    >
                      <Typography variant="subtitle2" sx={{ fontWeight: 700 }}>
                        {p.employeeName}
                      </Typography>
                      <Chip
                        label={p.status}
                        size="small"
                        color={p.status === 'Completed' ? 'success' : 'primary'}
                        sx={{ height: 20, fontSize: '0.65rem' }}
                      />
                    </Box>

                    <Typography variant="body2" sx={{ whiteSpace: 'pre-wrap', mb: 1 }}>
                      {p.goalsMarkdown}
                    </Typography>

                    {p.keyDeliverables && (
                      <Typography variant="caption" color="text.secondary" display="block">
                        Deliverables: {p.keyDeliverables}
                      </Typography>
                    )}
                  </Paper>
                ))}
              </Box>
            )}
          </Paper>
        </Grid>
      </Grid>
    </Box>
  );
};
