import React, { useState } from 'react';
import Dialog from '@mui/material/Dialog';
import DialogTitle from '@mui/material/DialogTitle';
import DialogContent from '@mui/material/DialogContent';
import DialogActions from '@mui/material/DialogActions';
import Button from '@mui/material/Button';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import BlockIcon from '@mui/icons-material/Block';
import { WorkItemDto, WorkItemReviewQueueDto } from '../../types';
import { reviewsApi } from '../../services/reviewsApi';

interface DenyModalProps {
  item: WorkItemReviewQueueDto | null;
  open: boolean;
  onClose: () => void;
  onSuccess: (updated: WorkItemDto) => void;
}

export const DenyModal: React.FC<DenyModalProps> = ({ item, open, onClose, onSuccess }) => {
  const [reason, setReason] = useState('');
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  if (!item) return null;

  const trimmedReason = reason.trim();
  const isValidLength = trimmedReason.length >= 5;

  const handleDeny = async () => {
    const itemId = item.id || item.workItemId;
    if (!itemId) {
      setError('Valid work item ID is required.');
      return;
    }
    if (!isValidLength) {
      setError('Denial reason must be at least 5 characters.');
      return;
    }

    setIsSubmitting(true);
    setError(null);

    try {
      const res = await reviewsApi.denyReview(itemId, {
        reason: trimmedReason,
      });
      onSuccess(res);
      handleModalClose();
    } catch (err: any) {
      const detail = err?.response?.data?.detail || err?.message || 'Failed to deny review.';
      setError(detail);
    } finally {
      setIsSubmitting(false);
    }
  };

  const handleModalClose = () => {
    setReason('');
    setError(null);
    onClose();
  };

  return (
    <Dialog open={open} onClose={handleModalClose} maxWidth="sm" fullWidth>
      <DialogTitle sx={{ display: 'flex', alignItems: 'center', gap: 1, fontWeight: 700 }}>
        <BlockIcon color="error" />
        Deny Work Item
      </DialogTitle>

      <DialogContent dividers>
        <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
          {error && <Alert severity="error">{error}</Alert>}

          <Box sx={{ p: 1.5, bgcolor: 'action.hover', borderRadius: 2 }}>
            <Typography variant="subtitle2" sx={{ fontWeight: 600 }}>
              {item.code || item.workItemCode}: {item.title || item.workItemTitle}
            </Typography>
            <Typography variant="caption" color="text.secondary">
              Creator: {item.creatorName || item.createdByName || 'Unknown'} &bull; Project: {item.projectName}
            </Typography>
          </Box>

          <Alert severity="error">
            <strong>Warning:</strong> Denying this work item will transition it to{' '}
            <strong>Denied</strong>, send an email notification to{' '}
            <strong>{item.creatorName || item.createdByName || 'Creator'}</strong>, and permanently <strong>block</strong> external
            synchronization to Azure DevOps.
          </Alert>

          <Box>
            <TextField
              label="Mandatory Denial Reason"
              value={reason}
              onChange={(e) => setReason(e.target.value)}
              multiline
              rows={4}
              placeholder="State why this work item is rejected (e.g. out of scope, duplicate initiative)..."
              fullWidth
              required
              autoFocus
              error={reason.length > 0 && !isValidLength}
              helperText={
                <Box
                  component="span"
                  sx={{ display: 'flex', justifyContent: 'space-between', width: '100%' }}
                >
                  <span>
                    {!isValidLength && reason.length > 0
                      ? 'At least 5 characters required.'
                      : 'Provide an explicit reason for denial.'}
                  </span>
                  <span>{trimmedReason.length} / 5 chars min</span>
                </Box>
              }
            />
          </Box>
        </Box>
      </DialogContent>

      <DialogActions sx={{ px: 3, py: 2 }}>
        <Button onClick={handleModalClose} disabled={isSubmitting}>
          Cancel
        </Button>
        <Button
          onClick={handleDeny}
          variant="contained"
          color="error"
          disabled={!isValidLength || isSubmitting}
        >
          {isSubmitting ? 'Denying...' : 'Deny & Block Sync'}
        </Button>
      </DialogActions>
    </Dialog>
  );
};
