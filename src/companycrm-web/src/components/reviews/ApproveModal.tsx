import React, { useState } from 'react';
import Dialog from '@mui/material/Dialog';
import DialogTitle from '@mui/material/DialogTitle';
import DialogContent from '@mui/material/DialogContent';
import DialogActions from '@mui/material/DialogActions';
import Button from '@mui/material/Button';
import TextField from '@mui/material/TextField';
import FormControl from '@mui/material/FormControl';
import InputLabel from '@mui/material/InputLabel';
import Select from '@mui/material/Select';
import MenuItem from '@mui/material/MenuItem';
import Typography from '@mui/material/Typography';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import CheckCircleOutlineIcon from '@mui/icons-material/CheckCircleOutline';
import OpenInNewIcon from '@mui/icons-material/OpenInNew';
import { WorkItemDto, WorkItemReviewQueueDto, WorkItemStatus } from '../../types';
import { reviewsApi } from '../../services/reviewsApi';

interface ApproveModalProps {
  item: WorkItemReviewQueueDto | null;
  open: boolean;
  onClose: () => void;
  onSuccess: (updated: WorkItemDto) => void;
}

export const ApproveModal: React.FC<ApproveModalProps> = ({ item, open, onClose, onSuccess }) => {
  const [startingStatus, setStartingStatus] = useState<WorkItemStatus>('Todo');
  const [approvalNote, setApprovalNote] = useState('');
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [syncedItem, setSyncedItem] = useState<WorkItemDto | null>(null);

  if (!item) return null;

  const handleApprove = async () => {
    const itemId = item.id || item.workItemId;
    if (!itemId) {
      setError('Valid work item ID is required.');
      return;
    }

    setIsSubmitting(true);
    setError(null);

    try {
      const res = await reviewsApi.approveReview(itemId, {
        startingStatus,
        approvalNote: approvalNote.trim() || null,
      });
      setSyncedItem(res);
      onSuccess(res);
    } catch (err: any) {
      const detail = err?.response?.data?.detail || err?.message || 'Failed to approve review.';
      setError(detail);
    } finally {
      setIsSubmitting(false);
    }
  };

  const handleModalClose = () => {
    setSyncedItem(null);
    setApprovalNote('');
    setError(null);
    onClose();
  };

  return (
    <Dialog open={open} onClose={handleModalClose} maxWidth="sm" fullWidth>
      <DialogTitle sx={{ display: 'flex', alignItems: 'center', gap: 1, fontWeight: 700 }}>
        <CheckCircleOutlineIcon color="success" />
        Approve Work Item Review
      </DialogTitle>

      <DialogContent dividers>
        {syncedItem ? (
          <Box sx={{ py: 2 }}>
            <Alert severity="success" sx={{ mb: 2 }}>
              Work item <strong>{syncedItem.code}</strong> approved and transitioned to{' '}
              <strong>{syncedItem.status}</strong>!
            </Alert>
            {syncedItem.externalId ? (
              <Box sx={{ p: 2, bgcolor: 'action.hover', borderRadius: 2 }}>
                <Typography variant="subtitle2" sx={{ fontWeight: 600 }}>
                  Azure DevOps Synchronization Triggered:
                </Typography>
                <Typography variant="body2" sx={{ mt: 0.5 }}>
                  External ID: <strong>{syncedItem.externalId}</strong>
                </Typography>
                {syncedItem.externalUrl && (
                  <Button
                    size="small"
                    startIcon={<OpenInNewIcon />}
                    href={syncedItem.externalUrl}
                    target="_blank"
                    sx={{ mt: 1 }}
                  >
                    Open in Azure DevOps
                  </Button>
                )}
              </Box>
            ) : (
              <Typography variant="body2" color="text.secondary">
                Item moved to backlog/todo. No external sync configured or required.
              </Typography>
            )}
          </Box>
        ) : (
          <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
            {error && <Alert severity="error">{error}</Alert>}

            <Box sx={{ p: 1.5, bgcolor: 'action.hover', borderRadius: 2 }}>
              <Typography variant="subtitle2" sx={{ fontWeight: 600 }}>
                {item.code || item.workItemCode}: {item.title || item.workItemTitle}
              </Typography>
              <Typography variant="caption" color="text.secondary">
                Created by {item.creatorName || item.createdByName || 'Unknown'} &bull; Project: {item.projectName}
              </Typography>
            </Box>

            <Alert severity="info">
              Approving this work item unlocks it on the Kanban board and immediately triggers an
              outbound sync push to Azure DevOps.
            </Alert>

            <FormControl fullWidth size="small">
              <InputLabel>Starting Status</InputLabel>
              <Select
                value={startingStatus}
                label="Starting Status"
                onChange={(e) => setStartingStatus(e.target.value as WorkItemStatus)}
              >
                <MenuItem value="Todo">Todo (Ready for development)</MenuItem>
                <MenuItem value="Backlog">Backlog (Prioritized pool)</MenuItem>
              </Select>
            </FormControl>

            <TextField
              label="Approval Note (Optional)"
              value={approvalNote}
              onChange={(e) => setApprovalNote(e.target.value)}
              multiline
              rows={3}
              placeholder="e.g. Scope looks good, ready to pick up..."
              fullWidth
            />
          </Box>
        )}
      </DialogContent>

      <DialogActions sx={{ px: 3, py: 2 }}>
        {syncedItem ? (
          <Button onClick={handleModalClose} variant="contained">
            Close
          </Button>
        ) : (
          <>
            <Button onClick={handleModalClose} disabled={isSubmitting}>
              Cancel
            </Button>
            <Button
              onClick={handleApprove}
              variant="contained"
              color="success"
              disabled={isSubmitting}
            >
              {isSubmitting ? 'Approving & Syncing...' : 'Approve & Push to ADO'}
            </Button>
          </>
        )}
      </DialogActions>
    </Dialog>
  );
};
