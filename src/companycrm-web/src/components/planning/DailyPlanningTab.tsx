import React, { useState, useEffect, useCallback, useMemo } from 'react';
import Box from '@mui/material/Box';
import Typography from '@mui/material/Typography';
import Paper from '@mui/material/Paper';
import TextField from '@mui/material/TextField';
import Button from '@mui/material/Button';
import IconButton from '@mui/material/IconButton';
import Alert from '@mui/material/Alert';
import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableContainer from '@mui/material/TableContainer';
import TableHead from '@mui/material/TableHead';
import TableRow from '@mui/material/TableRow';
import FormControl from '@mui/material/FormControl';
import Select from '@mui/material/Select';
import MenuItem from '@mui/material/MenuItem';
import Checkbox from '@mui/material/Checkbox';
import CircularProgress from '@mui/material/CircularProgress';
import AddIcon from '@mui/icons-material/Add';
import DeleteIcon from '@mui/icons-material/Delete';
import SendIcon from '@mui/icons-material/Send';
import CheckCircleOutlineIcon from '@mui/icons-material/CheckCircleOutline';
import WarningAmberIcon from '@mui/icons-material/WarningAmber';
import InfoOutlinedIcon from '@mui/icons-material/InfoOutlined';
import {
  DailyPlanDto,
  SubmitDailyPlanItemDto,
  SubmitDailyPlanRequestDto,
  WorkItemDto,
} from '../../types';
import { planningApi } from '../../services/planningApi';
import { workItemsApi } from '../../services/workItemsApi';
import { useAuth } from '../../contexts/AuthContext';
import { useProject } from '../../contexts/ProjectContext';
import { useSignalR } from '../../contexts/SignalRContext';

export const DailyPlanningTab: React.FC = () => {
  const { currentUser } = useAuth();
  const { activeProject } = useProject();
  const { subscribeToDailyPlanSubmitted } = useSignalR();

  const [planDate, setPlanDate] = useState(() => new Date().toISOString().split('T')[0]);
  const [existingPlan, setExistingPlan] = useState<DailyPlanDto | null>(null);
  const [projectWorkItems, setProjectWorkItems] = useState<WorkItemDto[]>([]);
  const [isLoading, setIsLoading] = useState(true);

  // Form state
  const [items, setItems] = useState<SubmitDailyPlanItemDto[]>([
    { workItemId: null, customTaskTitle: '', plannedHours: 4.0, goalNote: '', isCompleted: false },
  ]);
  const [blockers, setBlockers] = useState('');
  const [summaryNotes, setSummaryNotes] = useState('');
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [validationError, setValidationError] = useState<string | null>(null);
  const [feedback, setFeedback] = useState<{ severity: 'success' | 'error'; message: string } | null>(
    null
  );

  const loadDailyPlan = useCallback(async () => {
    setIsLoading(true);
    try {
      const plans = await planningApi.getDailyPlans({
        date: planDate,
        employeeId: currentUser?.id,
      });
      const myPlan = plans.find((p) => p.employeeId === currentUser?.id) || null;
      setExistingPlan(myPlan);

      if (myPlan && myPlan.items.length > 0) {
        setItems(
          myPlan.items.map((i) => ({
            workItemId: i.workItemId,
            customTaskTitle: i.customTaskTitle || '',
            plannedHours: i.plannedHours,
            goalNote: i.goalNote || '',
            isCompleted: i.isCompleted,
            sortOrder: i.sortOrder,
          }))
        );
        setBlockers(myPlan.blockers || '');
        setSummaryNotes(myPlan.summaryNotes || '');
      } else {
        setItems([
          { workItemId: null, customTaskTitle: '', plannedHours: 4.0, goalNote: '', isCompleted: false },
        ]);
        setBlockers('');
        setSummaryNotes('');
      }
    } catch (err) {
      console.error('Failed to load daily plan:', err);
    } finally {
      setIsLoading(false);
    }
  }, [planDate, currentUser]);

  useEffect(() => {
    loadDailyPlan();
  }, [loadDailyPlan]);

  // Load project work items for picker
  useEffect(() => {
    if (activeProject) {
      workItemsApi
        .getWorkItems({ projectId: activeProject.id, pageSize: 100 })
        .then((res) => setProjectWorkItems(res.items))
        .catch(console.error);
    }
  }, [activeProject]);

  // Subscribe to SignalR DailyPlanSubmitted
  useEffect(() => {
    const unsubscribe = subscribeToDailyPlanSubmitted((evt) => {
      if (evt.planDate === planDate && evt.employeeId === currentUser?.id) {
        loadDailyPlan();
      }
    });
    return unsubscribe;
  }, [subscribeToDailyPlanSubmitted, planDate, currentUser, loadDailyPlan]);

  const handleAddItem = () => {
    setItems((prev) => [
      ...prev,
      { workItemId: null, customTaskTitle: '', plannedHours: 2.0, goalNote: '', isCompleted: false },
    ]);
  };

  const handleRemoveItem = (index: number) => {
    setItems((prev) => prev.filter((_, i) => i !== index));
  };

  const handleItemChange = (index: number, field: keyof SubmitDailyPlanItemDto, value: any) => {
    setItems((prev) => {
      const next = [...prev];
      next[index] = { ...next[index], [field]: value };
      return next;
    });
  };

  const totalPlannedHours = useMemo(() => {
    return items.reduce((sum, item) => sum + (Number(item.plannedHours) || 0), 0);
  }, [items]);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setValidationError(null);
    setFeedback(null);

    // Validate planned hours: each item between 0.1 and 24.0
    if (items.length === 0) {
      setValidationError('At least one task item must be planned.');
      return;
    }

    for (let i = 0; i < items.length; i++) {
      const item = items[i];
      const hours = Number(item.plannedHours);
      if (isNaN(hours) || hours < 0.1 || hours > 24.0) {
        setValidationError(`Item #${i + 1} planned hours must be between 0.1 and 24.0.`);
        return;
      }
      if (!item.workItemId && !item.customTaskTitle?.trim()) {
        setValidationError(`Item #${i + 1} requires either a linked Work Item or a custom title.`);
        return;
      }
    }

    setIsSubmitting(true);

    try {
      const dto: SubmitDailyPlanRequestDto = {
        planDate,
        blockers: blockers.trim() || null,
        summaryNotes: summaryNotes.trim() || null,
        projectId: activeProject?.id ?? null,
        items: items.map((item, idx) => ({
          workItemId: item.workItemId || null,
          customTaskTitle: item.customTaskTitle?.trim() || null,
          plannedHours: Number(item.plannedHours),
          goalNote: item.goalNote?.trim() || null,
          isCompleted: item.isCompleted || false,
          sortOrder: idx,
        })),
      };

      const submitted = await planningApi.submitDailyPlan(dto);
      setExistingPlan(submitted);
      setFeedback({
        severity: 'success',
        message: `Daily plan submitted successfully! Status: ${
          submitted.isSubmittedOnTime ? 'On Time' : 'Submitted Past Cutoff'
        }.`,
      });
      loadDailyPlan();
    } catch (err: any) {
      setFeedback({
        severity: 'error',
        message: err?.response?.data?.detail || err?.message || 'Failed to submit daily plan.',
      });
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', gap: 3 }}>
      {/* Date Picker & On-Time Status Bar */}
      <Paper
        variant="outlined"
        sx={{
          p: 2.5,
          borderRadius: 2,
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          flexWrap: 'wrap',
          gap: 2,
        }}
      >
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 2 }}>
          <TextField
            size="small"
            type="date"
            label="Plan Date"
            value={planDate}
            onChange={(e) => setPlanDate(e.target.value)}
            InputLabelProps={{ shrink: true }}
            sx={{ width: 170 }}
          />

          <Button
            size="small"
            variant="outlined"
            onClick={() => setPlanDate(new Date().toISOString().split('T')[0])}
            sx={{ fontWeight: 600 }}
          >
            Today
          </Button>
        </Box>

        {/* On-Time Status Alert Badge */}
        {existingPlan ? (
          existingPlan.isSubmittedOnTime ? (
            <Alert
              severity="success"
              icon={<CheckCircleOutlineIcon />}
              sx={{ py: 0.5, px: 2, fontWeight: 600 }}
            >
              Submitted On-Time (Checked at {new Date(existingPlan.submittedAt).toLocaleTimeString()})
            </Alert>
          ) : (
            <Alert
              severity="warning"
              icon={<WarningAmberIcon />}
              sx={{ py: 0.5, px: 2, fontWeight: 600 }}
            >
              Submitted After Cutoff Deadline (at {new Date(existingPlan.submittedAt).toLocaleTimeString()})
            </Alert>
          )
        ) : (
          <Alert severity="info" icon={<InfoOutlinedIcon />} sx={{ py: 0.5, px: 2, fontWeight: 500 }}>
            Daily plan not yet submitted for this date.
          </Alert>
        )}
      </Paper>

      {validationError && <Alert severity="error">{validationError}</Alert>}
      {feedback && (
        <Alert severity={feedback.severity} onClose={() => setFeedback(null)}>
          {feedback.message}
        </Alert>
      )}

      {/* Daily Plan Check-In Form */}
      <Paper variant="outlined" sx={{ p: 3, borderRadius: 2 }}>
        <Typography variant="h6" sx={{ fontWeight: 700, mb: 2 }}>
          Daily Check-In & Planned Work Items
        </Typography>

        <form onSubmit={handleSubmit}>
          {/* Linked Tasks Table */}
          <TableContainer component={Paper} variant="outlined" sx={{ mb: 2.5, borderRadius: 2 }}>
            <Table size="small">
              <TableHead sx={{ bgcolor: 'action.hover' }}>
                <TableRow>
                  <TableCell sx={{ fontWeight: 700, width: 280 }}>Linked Work Item</TableCell>
                  <TableCell sx={{ fontWeight: 700 }}>Custom Task Title (Optional)</TableCell>
                  <TableCell sx={{ fontWeight: 700, width: 120 }}>Planned (h)</TableCell>
                  <TableCell sx={{ fontWeight: 700 }}>Goal / Standup Note</TableCell>
                  <TableCell sx={{ fontWeight: 700, width: 90 }}>Done?</TableCell>
                  <TableCell align="right" sx={{ fontWeight: 700, width: 60 }}>
                    Remove
                  </TableCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {items.map((item, idx) => (
                  <TableRow key={idx}>
                    <TableCell>
                      <FormControl fullWidth size="small">
                        <Select
                          value={item.workItemId || ''}
                          onChange={(e) =>
                            handleItemChange(idx, 'workItemId', e.target.value || null)
                          }
                          displayEmpty
                        >
                          <MenuItem value="">
                            <em>(Custom non-work-item task)</em>
                          </MenuItem>
                          {projectWorkItems.map((wi) => (
                            <MenuItem key={wi.id} value={wi.id}>
                              {wi.code}: {wi.title}
                            </MenuItem>
                          ))}
                        </Select>
                      </FormControl>
                    </TableCell>

                    <TableCell>
                      <TextField
                        size="small"
                        fullWidth
                        placeholder="e.g. Architecture review, standup..."
                        value={item.customTaskTitle || ''}
                        onChange={(e) =>
                          handleItemChange(idx, 'customTaskTitle', e.target.value)
                        }
                      />
                    </TableCell>

                    <TableCell>
                      <TextField
                        size="small"
                        type="number"
                        value={item.plannedHours}
                        onChange={(e) =>
                          handleItemChange(idx, 'plannedHours', parseFloat(e.target.value) || 0)
                        }
                        inputProps={{ min: 0.1, max: 24.0, step: 0.5 }}
                        required
                      />
                    </TableCell>

                    <TableCell>
                      <TextField
                        size="small"
                        fullWidth
                        placeholder="Target outcome..."
                        value={item.goalNote || ''}
                        onChange={(e) => handleItemChange(idx, 'goalNote', e.target.value)}
                      />
                    </TableCell>

                    <TableCell>
                      <Checkbox
                        size="small"
                        checked={item.isCompleted || false}
                        onChange={(e) => handleItemChange(idx, 'isCompleted', e.target.checked)}
                      />
                    </TableCell>

                    <TableCell align="right">
                      <IconButton
                        size="small"
                        color="error"
                        onClick={() => handleRemoveItem(idx)}
                        disabled={items.length <= 1}
                      >
                        <DeleteIcon fontSize="small" />
                      </IconButton>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </TableContainer>

          {/* Action Row */}
          <Box
            sx={{
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'space-between',
              mb: 3,
            }}
          >
            <Button
              size="small"
              variant="outlined"
              startIcon={<AddIcon />}
              onClick={handleAddItem}
              sx={{ fontWeight: 600 }}
            >
              Add Planned Task Row
            </Button>

            <Typography variant="subtitle2" sx={{ fontWeight: 700 }}>
              Total Planned Hours:{' '}
              <Box component="span" sx={{ color: 'primary.main', fontWeight: 800 }}>
                {totalPlannedHours.toFixed(1)}h
              </Box>
            </Typography>
          </Box>

          {/* Blockers & Summary Notes */}
          <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2, mb: 3 }}>
            <TextField
              label="Blockers & Dependencies (Optional)"
              multiline
              rows={2}
              placeholder="List any impediments, waiting items, or external blockers..."
              value={blockers}
              onChange={(e) => setBlockers(e.target.value)}
              fullWidth
            />

            <TextField
              label="Summary Notes / End of Day Log"
              multiline
              rows={2}
              placeholder="Additional comments or progress highlights..."
              value={summaryNotes}
              onChange={(e) => setSummaryNotes(e.target.value)}
              fullWidth
            />
          </Box>

          <Box sx={{ display: 'flex', justifyContent: 'flex-end' }}>
            <Button
              type="submit"
              variant="contained"
              size="large"
              startIcon={isSubmitting ? <CircularProgress size={20} color="inherit" /> : <SendIcon />}
              disabled={isSubmitting}
              sx={{ fontWeight: 700, px: 4 }}
            >
              {isSubmitting ? 'Submitting...' : 'Submit Daily Plan'}
            </Button>
          </Box>
        </form>
      </Paper>
    </Box>
  );
};
