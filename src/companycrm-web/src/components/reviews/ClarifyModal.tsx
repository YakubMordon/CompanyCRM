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
import HelpOutlineIcon from '@mui/icons-material/HelpOutline';
import { WorkItemDto, WorkItemReviewQueueDto } from '../../types';
import { reviewsApi } from '../../services/reviewsApi';

interface ClarifyModalProps {
  item: WorkItemReviewQueueDto | null;
  open: boolean;
  onClose: () => void;
  onSuccess: (updated: WorkItemDto) => void;
}

export const ClarifyModal: React.FC<ClarifyModalProps> = ({ item, open, onClose, onSuccess }) => {
  const [comment, setComment] = useState('');
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  if (!item) return null;

  const trimmedComment = comment.trim();
  const isValidLength = trimmedComment.length >= 5;

  const handleClarify = async () => {
    const itemId = item.id || item.workItemId;
    if (!itemId) {
      setError('Valid work item ID is required.');
      return;
    }
    if (!isValidLength) {
      setError('Clarification comment must be at least 5 characters.');
      return;
    }

    setIsSubmitting(true);
    setError(null);

    try {
      const res = await reviewsApi.requestClarification(itemId, {
        comment: trimmedComment,
      });
      onSuccess(res);
      handleModalClose();
    } catch (err: any) {
      const detail =
        err?.response?.data?.detail || err?.message || 'Failed to submit clarification request.';
      setError(detail);
    } finally {
      setIsSubmitting(false);
    }
  };

  const handleModalClose = () => {
    setComment('');
    setError(null);
    onClose();
  };

  return (
    <Dialog open={open} onClose={handleModalClose} maxWidth="sm" fullWidth>
      <DialogTitle sx={{ display: 'flex', alignItems: 'center', gap: 1, fontWeight: 700 }}>
        <HelpOutlineIcon color="warning" />
        Request Clarification from Creator
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

          <Alert severity="warning">
            This will transition the work item to <strong>Clarification Requested</strong>, send an
            email notification to <strong>{item.creatorName || item.createdByName || 'Creator'}</strong>, and keep the item locked on
            the Kanban board until changes are addressed.
          </Alert>

          <Box>
            <TextField
              label="Clarification Note / Questions"
              value={comment}
              onChange={(e) => setComment(e.target.value)}
              multiline
              rows={4}
              placeholder="Please specify acceptance criteria, test environment, or missing details..."
              fullWidth
              required
              autoFocus
              error={comment.length > 0 && !isValidLength}
              helperText={
                <Box
                  component="span"
                  sx={{ display: 'flex', justifyContent: 'space-between', width: '100%' }}
                >
                  <span>
                    {!isValidLength && comment.length > 0
                      ? 'At least 5 characters required.'
                      : 'Provide actionable feedback to the creator.'}
                  </span>
                  <span>{trimmedComment.length} / 5 chars min</span>
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
          onClick={handleClarify}
          variant="contained"
          color="warning"
          disabled={!isValidLength || isSubmitting}
        >
          {isSubmitting ? 'Sending Request...' : 'Send Clarification Request'}
        </Button>
      </DialogActions>
    </Dialog>
  );
};
