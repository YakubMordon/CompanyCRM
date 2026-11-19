import React, { useState, useEffect, useCallback } from 'react';
import Box from '@mui/material/Box';
import Typography from '@mui/material/Typography';
import Paper from '@mui/material/Paper';
import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableContainer from '@mui/material/TableContainer';
import TableHead from '@mui/material/TableHead';
import TableRow from '@mui/material/TableRow';
import Button from '@mui/material/Button';
import FormControlLabel from '@mui/material/FormControlLabel';
import Switch from '@mui/material/Switch';
import CircularProgress from '@mui/material/CircularProgress';
import Chip from '@mui/material/Chip';
import Avatar from '@mui/material/Avatar';
import Tooltip from '@mui/material/Tooltip';
import IconButton from '@mui/material/IconButton';
import RefreshIcon from '@mui/icons-material/Refresh';
import CheckCircleIcon from '@mui/icons-material/CheckCircle';
import HelpOutlineIcon from '@mui/icons-material/HelpOutline';
import BlockIcon from '@mui/icons-material/Block';
import { WorkItemReviewQueueDto } from '../types';
import { reviewsApi } from '../services/reviewsApi';
import { useProject } from '../contexts/ProjectContext';
import { useSignalR } from '../contexts/SignalRContext';
import { StatusChip } from '../components/common/StatusChip';
import { ApproveModal } from '../components/reviews/ApproveModal';
import { ClarifyModal } from '../components/reviews/ClarifyModal';
import { DenyModal } from '../components/reviews/DenyModal';

export const ReviewQueuePage: React.FC = () => {
  const { activeProject } = useProject();
  const { subscribeToReviewQueueUpdates } = useSignalR();

  const [queue, setQueue] = useState<WorkItemReviewQueueDto[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [assignedToMeOnly, setAssignedToMeOnly] = useState(false);

  // Modal states
  const [selectedItem, setSelectedItem] = useState<WorkItemReviewQueueDto | null>(null);
  const [isApproveOpen, setIsApproveOpen] = useState(false);
  const [isClarifyOpen, setIsClarifyOpen] = useState(false);
  const [isDenyOpen, setIsDenyOpen] = useState(false);

  const loadQueue = useCallback(async () => {
    try {
      setIsLoading(true);
      const data = await reviewsApi.getReviewQueue(activeProject?.id, assignedToMeOnly);
      setQueue(data);
    } catch (err) {
      console.error('Failed to load review queue:', err);
    } finally {
      setIsLoading(false);
    }
  }, [activeProject, assignedToMeOnly]);

  useEffect(() => {
    loadQueue();
  }, [loadQueue]);

  // Subscribe to SignalR ReviewQueueUpdated
  useEffect(() => {
    const unsubscribe = subscribeToReviewQueueUpdates(() => {
      loadQueue();
    });
    return unsubscribe;
  }, [subscribeToReviewQueueUpdates, loadQueue]);

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', gap: 3 }}>
      {/* Header & Controls */}
      <Box
        sx={{
          display: 'flex',
          flexWrap: 'wrap',
          alignItems: 'center',
          justifyContent: 'space-between',
          gap: 2,
        }}
      >
        <Box>
          <Typography variant="h5" sx={{ fontWeight: 700 }}>
            Review Queue
          </Typography>
          <Typography variant="body2" color="text.secondary">
            Approve, clarify, or deny work items before they enter active development or ADO sync.
          </Typography>
        </Box>

        <Box sx={{ display: 'flex', alignItems: 'center', gap: 2 }}>
          <FormControlLabel
            control={
              <Switch
                checked={assignedToMeOnly}
                onChange={(e) => setAssignedToMeOnly(e.target.checked)}
                color="primary"
              />
            }
            label={<Typography variant="body2">Assigned to Me Only</Typography>}
          />

          <Tooltip title="Refresh queue">
            <IconButton onClick={loadQueue} size="small">
              <RefreshIcon fontSize="small" />
            </IconButton>
          </Tooltip>
        </Box>
      </Box>

      {/* Review Queue Table */}
      {isLoading ? (
        <Box sx={{ display: 'flex', justifyContent: 'center', p: 8 }}>
          <CircularProgress />
        </Box>
      ) : queue.length === 0 ? (
        <Paper
          variant="outlined"
          sx={{
            p: 6,
            textAlign: 'center',
            borderRadius: 2,
            bgcolor: 'background.paper',
          }}
        >
          <CheckCircleIcon sx={{ fontSize: 48, color: 'success.main', mb: 1 }} />
          <Typography variant="h6" sx={{ fontWeight: 600 }}>
            Review Queue Clean!
          </Typography>
          <Typography variant="body2" color="text.secondary">
            No work items currently awaiting your review in this project.
          </Typography>
        </Paper>
      ) : (
        <TableContainer component={Paper} variant="outlined" sx={{ borderRadius: 2 }}>
          <Table>
            <TableHead sx={{ bgcolor: 'action.hover' }}>
              <TableRow>
                <TableCell sx={{ fontWeight: 700 }}>Item</TableCell>
                <TableCell sx={{ fontWeight: 700 }}>Project</TableCell>
                <TableCell sx={{ fontWeight: 700 }}>Type & Priority</TableCell>
                <TableCell sx={{ fontWeight: 700 }}>Creator</TableCell>
                <TableCell sx={{ fontWeight: 700 }}>Reviewer</TableCell>
                <TableCell sx={{ fontWeight: 700 }}>Review Status</TableCell>
                <TableCell sx={{ fontWeight: 700 }}>Feedback / Notes</TableCell>
                <TableCell align="right" sx={{ fontWeight: 700 }}>
                  Actions
                </TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {queue.map((row) => (
                <TableRow key={row.id || row.workItemId} hover>
                  <TableCell>
                    <Typography variant="body2" sx={{ fontWeight: 700, color: 'primary.main' }}>
                      {row.code || row.workItemCode}
                    </Typography>
                    <Typography variant="body2" sx={{ fontWeight: 500 }}>
                      {row.title || row.workItemTitle}
                    </Typography>
                  </TableCell>

                  <TableCell>
                    <Chip label={row.projectName} size="small" variant="outlined" />
                  </TableCell>

                  <TableCell>
                    <Box sx={{ display: 'flex', gap: 0.5 }}>
                      <StatusChip type={row.type} size="small" />
                      <StatusChip priority={row.priority} size="small" />
                    </Box>
                  </TableCell>

                  <TableCell>
                    <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
                      <Avatar sx={{ width: 24, height: 24, fontSize: 11, bgcolor: 'primary.dark' }}>
                        {(row.creatorName || row.createdByName || 'U').charAt(0)}
                      </Avatar>
                      <Typography variant="body2">{row.creatorName || row.createdByName || 'Unknown'}</Typography>
                    </Box>
                  </TableCell>

                  <TableCell>
                    <Typography variant="body2">{row.reviewerName || 'Unassigned'}</Typography>
                  </TableCell>

                  <TableCell>
                    <StatusChip reviewStatus={row.reviewStatus} size="small" />
                  </TableCell>

                  <TableCell sx={{ maxWidth: 200 }}>
                    {(row.reviewComment || row.latestComment) ? (
                      <Typography
                        variant="caption"
                        color="text.secondary"
                        sx={{
                          display: '-webkit-box',
                          WebkitLineClamp: 2,
                          WebkitBoxOrient: 'vertical',
                          overflow: 'hidden',
                        }}
                      >
                        {row.reviewComment || row.latestComment}
                      </Typography>
                    ) : (
                      <Typography variant="caption" color="text.secondary">
                        -
                      </Typography>
                    )}
                  </TableCell>

                  <TableCell align="right">
                    <Box sx={{ display: 'flex', justifyContent: 'flex-end', gap: 1 }}>
                      <Button
                        size="small"
                        variant="contained"
                        color="success"
                        startIcon={<CheckCircleIcon />}
                        onClick={() => {
                          setSelectedItem(row);
                          setIsApproveOpen(true);
                        }}
                        sx={{ fontWeight: 600 }}
                      >
                        Approve
                      </Button>

                      <Button
                        size="small"
                        variant="outlined"
                        color="warning"
                        startIcon={<HelpOutlineIcon />}
                        onClick={() => {
                          setSelectedItem(row);
                          setIsClarifyOpen(true);
                        }}
                        sx={{ fontWeight: 600 }}
                      >
                        Clarify
                      </Button>

                      <Button
                        size="small"
                        variant="outlined"
                        color="error"
                        startIcon={<BlockIcon />}
                        onClick={() => {
                          setSelectedItem(row);
                          setIsDenyOpen(true);
                        }}
                        sx={{ fontWeight: 600 }}
                      >
                        Deny
                      </Button>
                    </Box>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </TableContainer>
      )}

      {/* Modals */}
      <ApproveModal
        item={selectedItem}
        open={isApproveOpen}
        onClose={() => setIsApproveOpen(false)}
        onSuccess={() => loadQueue()}
      />

      <ClarifyModal
        item={selectedItem}
        open={isClarifyOpen}
        onClose={() => setIsClarifyOpen(false)}
        onSuccess={() => loadQueue()}
      />

      <DenyModal
        item={selectedItem}
        open={isDenyOpen}
        onClose={() => setIsDenyOpen(false)}
        onSuccess={() => loadQueue()}
      />
    </Box>
  );
};
