import React, { useState, useEffect } from 'react';
import Drawer from '@mui/material/Drawer';
import Box from '@mui/material/Box';
import Typography from '@mui/material/Typography';
import IconButton from '@mui/material/IconButton';
import CloseIcon from '@mui/icons-material/Close';
import Divider from '@mui/material/Divider';
import Chip from '@mui/material/Chip';
import Button from '@mui/material/Button';
import TextField from '@mui/material/TextField';
import FormControlLabel from '@mui/material/FormControlLabel';
import Checkbox from '@mui/material/Checkbox';
import Paper from '@mui/material/Paper';
import Avatar from '@mui/material/Avatar';
import PlayArrowIcon from '@mui/icons-material/PlayArrow';
import StopIcon from '@mui/icons-material/Stop';
import AccessTimeIcon from '@mui/icons-material/AccessTime';
import HistoryIcon from '@mui/icons-material/History';
import AddCircleOutlineIcon from '@mui/icons-material/AddCircleOutline';
import EditIcon from '@mui/icons-material/Edit';
import CloudUploadIcon from '@mui/icons-material/CloudUpload';
import CircularProgress from '@mui/material/CircularProgress';
import Alert from '@mui/material/Alert';
import {
  CreateManualTimeEntryDto,
  TimeEntryDto,
  WorkItemDto,
  WorkItemHistoryDto,
  WorkItemReviewQueueDto,
} from '../../types';
import { workItemsApi } from '../../services/workItemsApi';
import { syncApi } from '../../services/syncApi';
import { useAuth } from '../../contexts/AuthContext';
import { manualTimeEntrySchema, LIMITS } from '../../validation/schemas';
import { getApiErrorMessage } from '../../validation/errorMessage';
import { StatusChip } from '../common/StatusChip';
import { ApproveModal } from '../reviews/ApproveModal';
import { ClarifyModal } from '../reviews/ClarifyModal';
import { DenyModal } from '../reviews/DenyModal';
import { getHistoryActionLabel, getHistoryNote } from './workItemHelpers';

interface WorkItemDrawerProps {
  item: WorkItemDto | null;
  open: boolean;
  onClose: () => void;
  onItemUpdated: (item: WorkItemDto) => void;
  onEditItem?: (item: WorkItemDto) => void;
  activeTimer: TimeEntryDto | null;
  onRefreshActiveTimer: () => void;
}

export const WorkItemDrawer: React.FC<WorkItemDrawerProps> = ({
  item,
  open,
  onClose,
  onItemUpdated,
  onEditItem,
  activeTimer,
  onRefreshActiveTimer,
}) => {
  const { role } = useAuth();
  const canReview = role === 'TeamLead' || role === 'HigherManagement';
  const [isApproveOpen, setIsApproveOpen] = useState(false);
  const [isClarifyOpen, setIsClarifyOpen] = useState(false);
  const [isDenyOpen, setIsDenyOpen] = useState(false);

  const [history, setHistory] = useState<WorkItemHistoryDto[]>([]);
  const [isLoadingHistory, setIsLoadingHistory] = useState(false);
  const [isPushingAdo, setIsPushingAdo] = useState(false);
  const [pushStatusMessage, setPushStatusMessage] = useState<string | null>(null);

  // Live timer state
  const isCurrentItemRunning = Boolean(
    activeTimer && item && activeTimer.workItemId === item.id && activeTimer.isRunning
  );
  const [elapsedSeconds, setElapsedSeconds] = useState(0);
  const [isBillableLive, setIsBillableLive] = useState(true);
  const [timerNotes, setTimerNotes] = useState('');

  // Manual time entry form state
  const [manualMinutes, setManualMinutes] = useState('60');
  const [manualBillable, setManualBillable] = useState(true);
  const [manualNotes, setManualNotes] = useState('');
  const [manualDate, setManualDate] = useState(() => new Date().toISOString().split('T')[0]);
  const [isSubmittingTime, setIsSubmittingTime] = useState(false);
  const [timeError, setTimeError] = useState<string | null>(null);
  const [timeSuccess, setTimeSuccess] = useState<string | null>(null);

  // Load history when item changes
  useEffect(() => {
    if (item && open) {
      setIsLoadingHistory(true);
      workItemsApi
        .getWorkItemHistory(item.id)
        .then(setHistory)
        .catch(console.error)
        .finally(() => setIsLoadingHistory(false));
    }
  }, [item, open]);

  // Clock elapsed seconds when timer is active
  useEffect(() => {
    let interval: any = null;
    if (isCurrentItemRunning && activeTimer) {
      const startTime = new Date(activeTimer.startedAt).getTime();
      const updateElapsed = () => {
        const now = Date.now();
        const diffSec = Math.max(0, Math.floor((now - startTime) / 1000));
        setElapsedSeconds(diffSec);
      };
      updateElapsed();
      interval = setInterval(updateElapsed, 1000);
    } else {
      setElapsedSeconds(0);
    }
    return () => {
      if (interval) clearInterval(interval);
    };
  }, [isCurrentItemRunning, activeTimer]);

  const formatSeconds = (totalSec: number) => {
    const hours = Math.floor(totalSec / 3600);
    const minutes = Math.floor((totalSec % 3600) / 60);
    const seconds = totalSec % 60;
    return `${hours.toString().padStart(2, '0')}:${minutes
      .toString()
      .padStart(2, '0')}:${seconds.toString().padStart(2, '0')}`;
  };

  const handleStartTimer = async () => {
    if (!item) return;
    try {
      setTimeError(null);
      await workItemsApi.startTimer(item.id, { isBillable: isBillableLive });
      onRefreshActiveTimer();
      // Reload item
      const updated = await workItemsApi.getWorkItemById(item.id);
      onItemUpdated(updated);
      const h = await workItemsApi.getWorkItemHistory(item.id);
      setHistory(h);
    } catch (err: any) {
      setTimeError(err?.response?.data?.detail || err?.message || 'Failed to start timer');
    }
  };

  const handleStopTimer = async () => {
    if (!item) return;
    try {
      setTimeError(null);
      await workItemsApi.stopTimer(item.id, {
        notes: timerNotes.trim() || null,
        isBillable: isBillableLive,
      });
      setTimerNotes('');
      onRefreshActiveTimer();
      const updated = await workItemsApi.getWorkItemById(item.id);
      onItemUpdated(updated);
      const h = await workItemsApi.getWorkItemHistory(item.id);
      setHistory(h);
    } catch (err: any) {
      setTimeError(err?.response?.data?.detail || err?.message || 'Failed to stop timer');
    }
  };

  const handleManualTimeSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!item) return;

    const mins = parseInt(manualMinutes, 10);
    const validation = manualTimeEntrySchema.safeParse({
      durationMinutes: isNaN(mins) ? -1 : mins,
      notes: manualNotes,
    });

    if (!validation.success) {
      setTimeError(validation.error.issues[0]?.message || 'Invalid time entry');
      return;
    }

    setIsSubmittingTime(true);
    setTimeError(null);
    setTimeSuccess(null);

    try {
      const dto: CreateManualTimeEntryDto = {
        durationMinutes: mins,
        isBillable: manualBillable,
        notes: manualNotes.trim() || null,
        loggedDate: manualDate,
      };

      await workItemsApi.createManualTimeEntry(item.id, dto);
      setTimeSuccess(`Logged ${mins} minutes successfully.`);
      setManualNotes('');
      // Reload item & history
      const updated = await workItemsApi.getWorkItemById(item.id);
      onItemUpdated(updated);
      const h = await workItemsApi.getWorkItemHistory(item.id);
      setHistory(h);
    } catch (err: any) {
      setTimeError(getApiErrorMessage(err, 'Failed to log manual time'));
    } finally {
      setIsSubmittingTime(false);
    }
  };

  if (!item) return null;

  return (
    <Drawer
      anchor="right"
      open={open}
      onClose={onClose}
      PaperProps={{
        sx: { width: { xs: '100%', sm: 540 }, p: 3, boxSizing: 'border-box' },
      }}
    >
      {/* Header */}
      <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', mb: 2 }}>
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
          <Typography variant="h6" sx={{ fontWeight: 700, color: 'primary.main' }}>
            {item.code}
          </Typography>
          <StatusChip type={item.type} />
          <StatusChip status={item.status} />
        </Box>
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.5 }}>
          {onEditItem && (
            <Button
              size="small"
              startIcon={<EditIcon />}
              onClick={() => {
                onEditItem(item);
                onClose();
              }}
              variant="outlined"
              sx={{ mr: 1 }}
            >
              Edit
            </Button>
          )}
          <IconButton onClick={onClose} size="small">
            <CloseIcon />
          </IconButton>
        </Box>
      </Box>

      <Typography variant="h6" sx={{ fontWeight: 600, mb: 1 }}>
        {item.title}
      </Typography>

      {item.description && (
        <Typography variant="body2" color="text.secondary" sx={{ mb: 2, whiteSpace: 'pre-wrap' }}>
          {item.description}
        </Typography>
      )}

      {/* Meta tags */}
      <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 1, mb: 3 }}>
        <StatusChip priority={item.priority} />
        {item.storyPoints != null && (
          <Chip label={`${item.storyPoints} Story Points`} size="small" variant="outlined" />
        )}
        {item.estimatedHours != null && (
          <Chip label={`Est: ${item.estimatedHours}h`} size="small" variant="outlined" />
        )}
        <Chip
          label={`Total Logged: ${(item.totalLoggedMinutes / 60.0).toFixed(1)}h (${(
            item.billableMinutes / 60.0
          ).toFixed(1)}h billable)`}
          size="small"
          color="info"
          variant="outlined"
        />
        {item.reviewerName && (
          <Chip
            label={`Reviewer: ${item.reviewerName} (${item.reviewStatus})`}
            size="small"
            color={item.reviewStatus === 'Approved' ? 'success' : 'warning'}
            variant="outlined"
          />
        )}
        {item.externalId && (
          <Chip
            label={`ADO: ${item.externalId}`}
            size="small"
            color="secondary"
            variant="outlined"
          />
        )}
        <Button
          size="small"
          variant="outlined"
          color="secondary"
          startIcon={isPushingAdo ? <CircularProgress size={14} color="inherit" /> : <CloudUploadIcon />}
          disabled={isPushingAdo}
          onClick={async () => {
            try {
              setIsPushingAdo(true);
              setPushStatusMessage(null);
              const result = await syncApi.pushWorkItem(item.id);
              if (result.success || result.isSuccess) {
                setPushStatusMessage(`Pushed to ADO (${result.externalId || 'synced'})`);
                const updated = await workItemsApi.getWorkItemById(item.id);
                onItemUpdated(updated);
              } else {
                setPushStatusMessage(result.message || result.errorMessage || 'Push failed');
              }
            } catch (err: any) {
              setPushStatusMessage(err?.response?.data?.detail || err?.message || 'Push to ADO failed');
            } finally {
              setIsPushingAdo(false);
            }
          }}
          sx={{ height: 24, fontSize: '0.75rem', textTransform: 'none' }}
        >
          {isPushingAdo ? 'Pushing...' : 'Push to ADO'}
        </Button>
      </Box>

      {/* Review Actions for Management / Reviewers */}
      {canReview && (item.reviewStatus === 'Pending' || item.reviewStatus === 'ClarificationNeeded') && (
        <Paper
          variant="outlined"
          sx={{
            p: 2,
            mb: 3,
            borderRadius: 2,
            bgcolor: 'action.hover',
            borderColor: 'warning.main',
          }}
        >
          <Typography variant="subtitle2" sx={{ fontWeight: 700, mb: 1, color: 'warning.dark' }}>
            Task Review Pending Approval
          </Typography>
          <Typography variant="caption" color="text.secondary" display="block" sx={{ mb: 1.5 }}>
            This item requires review approval before proceeding to active backlog / sprint delivery.
          </Typography>
          <Box sx={{ display: 'flex', gap: 1, flexWrap: 'wrap' }}>
            <Button
              size="small"
              variant="contained"
              color="success"
              onClick={() => setIsApproveOpen(true)}
              sx={{ fontWeight: 600, textTransform: 'none' }}
            >
              Approve Task
            </Button>
            <Button
              size="small"
              variant="outlined"
              color="warning"
              onClick={() => setIsClarifyOpen(true)}
              sx={{ fontWeight: 600, textTransform: 'none' }}
            >
              Request Clarification
            </Button>
            <Button
              size="small"
              variant="outlined"
              color="error"
              onClick={() => setIsDenyOpen(true)}
              sx={{ fontWeight: 600, textTransform: 'none' }}
            >
              Deny
            </Button>
          </Box>
        </Paper>
      )}

      {pushStatusMessage && (
        <Alert
          severity={pushStatusMessage.includes('Pushed') ? 'success' : 'error'}
          onClose={() => setPushStatusMessage(null)}
          sx={{ mb: 2 }}
        >
          {pushStatusMessage}
        </Alert>
      )}

      <Divider sx={{ my: 2 }} />

      {/* Section A: Live Start/Stop Timer */}
      <Box sx={{ mb: 3 }}>
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, mb: 1.5 }}>
          <AccessTimeIcon color="primary" fontSize="small" />
          <Typography variant="subtitle2" sx={{ fontWeight: 700 }}>
            Live Start/Stop Timer
          </Typography>
        </Box>

        <Paper
          variant="outlined"
          sx={{
            p: 2,
            borderRadius: 2,
            bgcolor: isCurrentItemRunning ? 'action.hover' : 'background.paper',
            borderColor: isCurrentItemRunning ? 'success.main' : 'divider',
          }}
        >
          <Box
            sx={{
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'space-between',
              mb: 2,
            }}
          >
            <Box>
              <Typography variant="caption" color="text.secondary" display="block">
                Elapsed Time
              </Typography>
              <Typography
                variant="h4"
                sx={{
                  fontFamily: 'monospace',
                  fontWeight: 700,
                  color: isCurrentItemRunning ? 'success.main' : 'text.primary',
                }}
              >
                {formatSeconds(elapsedSeconds)}
              </Typography>
            </Box>

            <Box>
              {isCurrentItemRunning ? (
                <Button
                  variant="contained"
                  color="error"
                  startIcon={<StopIcon />}
                  onClick={handleStopTimer}
                  sx={{ fontWeight: 700 }}
                >
                  Stop Timer
                </Button>
              ) : (
                <Button
                  variant="contained"
                  color="success"
                  startIcon={<PlayArrowIcon />}
                  onClick={handleStartTimer}
                  sx={{ fontWeight: 700 }}
                >
                  Start Timer
                </Button>
              )}
            </Box>
          </Box>

          <Box sx={{ display: 'flex', alignItems: 'center', gap: 2 }}>
            <FormControlLabel
              control={
                <Checkbox
                  checked={isBillableLive}
                  onChange={(e) => setIsBillableLive(e.target.checked)}
                  size="small"
                  color="primary"
                />
              }
              label={<Typography variant="body2">Billable Time</Typography>}
            />

            {isCurrentItemRunning && (
              <TextField
                size="small"
                placeholder="Optional notes for this session..."
                value={timerNotes}
                onChange={(e) => setTimerNotes(e.target.value)}
                sx={{ flexGrow: 1 }}
              />
            )}
          </Box>
        </Paper>
      </Box>

      {/* Section B: Manual Time Entry Form */}
      <Box sx={{ mb: 3 }}>
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, mb: 1.5 }}>
          <AddCircleOutlineIcon color="primary" fontSize="small" />
          <Typography variant="subtitle2" sx={{ fontWeight: 700 }}>
            Log Manual Time Entry
          </Typography>
        </Box>

        <Paper variant="outlined" sx={{ p: 2, borderRadius: 2 }}>
          <form onSubmit={handleManualTimeSubmit}>
            {timeError && (
              <Alert severity="error" sx={{ mb: 1.5 }}>
                {timeError}
              </Alert>
            )}
            {timeSuccess && (
              <Alert severity="success" sx={{ mb: 1.5 }}>
                {timeSuccess}
              </Alert>
            )}

            <Box sx={{ display: 'flex', gap: 1.5, mb: 1.5 }}>
              <TextField
                size="small"
                type="number"
                label="Duration (minutes)"
                value={manualMinutes}
                onChange={(e) => setManualMinutes(e.target.value)}
                sx={{ width: 160 }}
                inputProps={{ min: 1, max: 1440 }}
                required
              />

              <TextField
                size="small"
                type="date"
                label="Date"
                value={manualDate}
                onChange={(e) => setManualDate(e.target.value)}
                sx={{ width: 170 }}
                InputLabelProps={{ shrink: true }}
                required
              />

              <FormControlLabel
                control={
                  <Checkbox
                    checked={manualBillable}
                    onChange={(e) => setManualBillable(e.target.checked)}
                    size="small"
                  />
                }
                label={<Typography variant="body2">Is Billable</Typography>}
              />
            </Box>

            <Box sx={{ display: 'flex', gap: 1.5 }}>
              <TextField
                size="small"
                placeholder="Work description / notes..."
                value={manualNotes}
                onChange={(e) => setManualNotes(e.target.value)}
                fullWidth
              />
              <Button
                type="submit"
                variant="outlined"
                disabled={isSubmittingTime}
                sx={{ whiteSpace: 'nowrap', fontWeight: 600 }}
              >
                Log Time
              </Button>
            </Box>
          </form>
        </Paper>
      </Box>

      <Divider sx={{ my: 2 }} />

      {/* Section C: WorkItemHistory Chronological Timeline */}
      <Box sx={{ flexGrow: 1, overflowY: 'auto' }}>
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, mb: 1.5 }}>
          <HistoryIcon color="primary" fontSize="small" />
          <Typography variant="subtitle2" sx={{ fontWeight: 700 }}>
            Audit History Timeline
          </Typography>
        </Box>

        {isLoadingHistory ? (
          <Typography variant="caption" color="text.secondary">
            Loading audit history...
          </Typography>
        ) : history.length === 0 ? (
          <Typography variant="caption" color="text.secondary">
            No history records yet.
          </Typography>
        ) : (
          <Box sx={{ display: 'flex', flexDirection: 'column', gap: 1.5 }}>
            {history.map((h) => {
              const actionLabel = getHistoryActionLabel(h);
              const noteText = getHistoryNote(h);
              return (
                <Paper
                  key={h.id}
                  variant="outlined"
                  sx={{ p: 1.5, borderRadius: 2, bgcolor: 'background.default' }}
                >
                  <Box
                    sx={{
                      display: 'flex',
                      alignItems: 'center',
                      justifyContent: 'space-between',
                      mb: 0.5,
                    }}
                  >
                    <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
                      <Avatar sx={{ width: 20, height: 20, fontSize: 10, bgcolor: 'primary.dark' }}>
                        {(h.changedByName || 'S').charAt(0)}
                      </Avatar>
                      <Typography variant="body2" sx={{ fontWeight: 600 }}>
                        {h.changedByName || 'System'}
                      </Typography>
                      <Chip
                        label={actionLabel}
                        size="small"
                        color="primary"
                        variant="outlined"
                        sx={{ height: 18, fontSize: '0.65rem' }}
                      />
                    </Box>
                    <Typography variant="caption" color="text.secondary">
                      {new Date(h.changedAt).toLocaleString()}
                    </Typography>
                  </Box>

                  {h.fieldName && (
                    <Typography variant="caption" color="text.secondary" display="block">
                      Field: <strong>{h.fieldName}</strong> ({h.oldValue || 'none'} &rarr;{' '}
                      <strong>{h.newValue || 'none'}</strong>)
                    </Typography>
                  )}

                  {noteText && (
                    <Typography variant="body2" sx={{ mt: 0.5, fontSize: '0.8rem' }}>
                      {noteText}
                    </Typography>
                  )}
                </Paper>
              );
            })}
          </Box>
        )}
      </Box>

      {/* Review Modals */}
      {item && (
        <>
          <ApproveModal
            item={
              {
                id: item.id,
                workItemId: item.id,
                code: item.code,
                workItemCode: item.code,
                title: item.title,
                workItemTitle: item.title,
                projectId: item.projectId,
                projectCode: item.code.split('-')[0] || 'PRJ',
                projectName: 'Project',
                type: item.type,
                priority: item.priority,
                creatorId: item.createdById || '',
                createdById: item.createdById || '',
                creatorName: item.createdByName || 'Unknown',
                createdByName: item.createdByName || 'Unknown',
                reviewerId: item.reviewerId,
                reviewerName: item.reviewerName,
                reviewStatus: item.reviewStatus,
                reviewComment: null,
                requestedAt: item.createdAt,
                createdAt: item.createdAt,
              } as unknown as WorkItemReviewQueueDto
            }
            open={isApproveOpen}
            onClose={() => setIsApproveOpen(false)}
            onSuccess={async () => {
              setIsApproveOpen(false);
              const updated = await workItemsApi.getWorkItemById(item.id);
              onItemUpdated(updated);
            }}
          />

          <ClarifyModal
            item={
              {
                id: item.id,
                workItemId: item.id,
                code: item.code,
                workItemCode: item.code,
                title: item.title,
                workItemTitle: item.title,
                projectId: item.projectId,
                projectCode: item.code.split('-')[0] || 'PRJ',
                projectName: 'Project',
                type: item.type,
                priority: item.priority,
                creatorId: item.createdById || '',
                createdById: item.createdById || '',
                creatorName: item.createdByName || 'Unknown',
                createdByName: item.createdByName || 'Unknown',
                reviewerId: item.reviewerId,
                reviewerName: item.reviewerName,
                reviewStatus: item.reviewStatus,
                reviewComment: null,
                requestedAt: item.createdAt,
                createdAt: item.createdAt,
              } as unknown as WorkItemReviewQueueDto
            }
            open={isClarifyOpen}
            onClose={() => setIsClarifyOpen(false)}
            onSuccess={async () => {
              setIsClarifyOpen(false);
              const updated = await workItemsApi.getWorkItemById(item.id);
              onItemUpdated(updated);
            }}
          />

          <DenyModal
            item={
              {
                id: item.id,
                workItemId: item.id,
                code: item.code,
                workItemCode: item.code,
                title: item.title,
                workItemTitle: item.title,
                projectId: item.projectId,
                projectCode: item.code.split('-')[0] || 'PRJ',
                projectName: 'Project',
                type: item.type,
                priority: item.priority,
                creatorId: item.createdById || '',
                createdById: item.createdById || '',
                creatorName: item.createdByName || 'Unknown',
                createdByName: item.createdByName || 'Unknown',
                reviewerId: item.reviewerId,
                reviewerName: item.reviewerName,
                reviewStatus: item.reviewStatus,
                reviewComment: null,
                requestedAt: item.createdAt,
                createdAt: item.createdAt,
              } as unknown as WorkItemReviewQueueDto
            }
            open={isDenyOpen}
            onClose={() => setIsDenyOpen(false)}
            onSuccess={async () => {
              setIsDenyOpen(false);
              const updated = await workItemsApi.getWorkItemById(item.id);
              onItemUpdated(updated);
            }}
          />
        </>
      )}
    </Drawer>
  );
};
