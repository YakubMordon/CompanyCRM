import React, { useState, useEffect, useCallback } from 'react';
import Paper from '@mui/material/Paper';
import Box from '@mui/material/Box';
import Typography from '@mui/material/Typography';
import Button from '@mui/material/Button';
import Chip from '@mui/material/Chip';
import Grid from '@mui/material/Grid';
import CircularProgress from '@mui/material/CircularProgress';
import CompareArrowsIcon from '@mui/icons-material/CompareArrows';
import CallMergeIcon from '@mui/icons-material/CallMerge';
import ContentCopyIcon from '@mui/icons-material/ContentCopy';
import SkipNextIcon from '@mui/icons-material/SkipNext';
import CheckCircleOutlineIcon from '@mui/icons-material/CheckCircleOutline';
import Alert from '@mui/material/Alert';
import { ConflictResolutionAction, SyncConflictDto } from '../../types';
import { syncApi } from '../../services/syncApi';
import { canMergeConflict } from './syncHelpers';

interface ConflictResolverProps {
  projectId: string;
}

export const ConflictResolver: React.FC<ConflictResolverProps> = ({ projectId }) => {
  const [conflicts, setConflicts] = useState<SyncConflictDto[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [resolvingId, setResolvingId] = useState<string | null>(null);
  const [actionMessage, setActionMessage] = useState<string | null>(null);

  const loadConflicts = useCallback(async () => {
    setIsLoading(true);
    try {
      const data = await syncApi.getConflicts(projectId);
      setConflicts(data.filter((c) => c.status === 'Pending'));
    } catch (err) {
      console.error('Failed to load conflicts:', err);
    } finally {
      setIsLoading(false);
    }
  }, [projectId]);

  useEffect(() => {
    loadConflicts();
  }, [loadConflicts]);

  const handleResolve = async (conflictId: string, action: ConflictResolutionAction) => {
    setResolvingId(conflictId);
    setActionMessage(null);

    try {
      const res = await syncApi.resolveConflict(conflictId, {
        action,
        resolutionNotes: `Resolved via UI with action: ${action}`,
      });
      setActionMessage(`Conflict resolved with action "${action}": ${res.message || 'Done.'}`);
      loadConflicts();
    } catch (err: any) {
      setActionMessage(
        err?.response?.data?.message || err?.response?.data?.detail || err?.message || 'Failed to resolve conflict.'
      );
    } finally {
      setResolvingId(null);
    }
  };

  if (isLoading) {
    return (
      <Paper variant="outlined" sx={{ p: 4, borderRadius: 2, textAlign: 'center' }}>
        <CircularProgress size={28} />
      </Paper>
    );
  }

  return (
    <Paper variant="outlined" sx={{ p: 3, borderRadius: 2 }}>
      <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', mb: 2 }}>
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
          <CompareArrowsIcon color="primary" />
          <Typography variant="h6" sx={{ fontWeight: 700 }}>
            Conflict & Deduplication Resolver
          </Typography>
        </Box>
        <Chip
          label={`${conflicts.length} Pending Conflicts`}
          color={conflicts.length > 0 ? 'warning' : 'success'}
          size="small"
          sx={{ fontWeight: 600 }}
        />
      </Box>

      {actionMessage && (
        <Alert severity="info" sx={{ mb: 2 }} onClose={() => setActionMessage(null)}>
          {actionMessage}
        </Alert>
      )}

      {conflicts.length === 0 ? (
        <Box sx={{ p: 4, textAlign: 'center', bgcolor: 'action.hover', borderRadius: 2 }}>
          <CheckCircleOutlineIcon color="success" sx={{ fontSize: 40, mb: 1 }} />
          <Typography variant="subtitle1" sx={{ fontWeight: 600 }}>
            All Items Synchronized
          </Typography>
          <Typography variant="body2" color="text.secondary">
            No deduplication conflicts or concurrent update clashes detected.
          </Typography>
        </Box>
      ) : (
        <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2.5 }}>
          {conflicts.map((conflict) => {
            const isProcessing = resolvingId === conflict.id;
            return (
              <Paper
                key={conflict.id}
                variant="outlined"
                sx={{
                  p: 2.5,
                  borderRadius: 2,
                  borderColor: 'warning.main',
                  borderWidth: 1.5,
                  bgcolor: 'background.default',
                }}
              >
                {/* Header with similarity badge */}
                <Box
                  sx={{
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'space-between',
                    mb: 2,
                    flexWrap: 'wrap',
                    gap: 1,
                  }}
                >
                  <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
                    <Typography variant="subtitle2" sx={{ fontWeight: 700 }}>
                      Conflict Type: {conflict.conflictType}
                    </Typography>
                    <Chip
                      label={`${Math.round(conflict.similarityScore * 100)}% Similarity Match`}
                      size="small"
                      color="warning"
                      sx={{ fontWeight: 700, fontSize: '0.7rem' }}
                    />
                  </Box>
                  <Typography variant="caption" color="text.secondary">
                    Detected: {new Date(conflict.detectedAt).toLocaleString()}
                  </Typography>
                </Box>

                {/* Side-by-Side Comparison */}
                <Grid container spacing={2} sx={{ mb: 2.5 }}>
                  {/* Left: Local CRM Item */}
                  <Grid item xs={12} md={6}>
                    <Paper
                      variant="outlined"
                      sx={{ p: 2, borderRadius: 2, bgcolor: 'background.paper', height: '100%' }}
                    >
                      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, mb: 1 }}>
                        <Chip
                          label="Local CRM Work Item"
                          size="small"
                          color="primary"
                          sx={{ fontWeight: 600, fontSize: '0.65rem' }}
                        />
                        <Typography variant="body2" sx={{ fontWeight: 700 }}>
                          {conflict.localWorkItemCode || 'Unlinked'}
                        </Typography>
                      </Box>
                      <Typography variant="subtitle2" sx={{ fontWeight: 600, mb: 1 }}>
                        {conflict.localTitle || 'No Local Title'}
                      </Typography>
                      {conflict.localStatus && (
                        <Typography variant="caption" color="text.secondary" display="block">
                          Status: <strong>{conflict.localStatus}</strong>
                        </Typography>
                      )}
                      {(conflict.localAssigneeName || conflict.localStatus) && (
                        <Typography variant="caption" color="text.secondary" display="block">
                          Assignee: {conflict.localAssigneeName || 'Unassigned'}
                        </Typography>
                      )}
                    </Paper>
                  </Grid>

                  {/* Right: Remote Azure DevOps Item */}
                  <Grid item xs={12} md={6}>
                    <Paper
                      variant="outlined"
                      sx={{ p: 2, borderRadius: 2, bgcolor: 'background.paper', height: '100%' }}
                    >
                      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, mb: 1 }}>
                        <Chip
                          label="Remote Azure DevOps"
                          size="small"
                          color="secondary"
                          sx={{ fontWeight: 600, fontSize: '0.65rem' }}
                        />
                        <Typography variant="body2" sx={{ fontWeight: 700 }}>
                          {conflict.externalId}
                        </Typography>
                      </Box>
                      <Typography variant="subtitle2" sx={{ fontWeight: 600, mb: 1 }}>
                        {conflict.externalTitle || 'No Remote Title'}
                      </Typography>
                      <Typography variant="caption" color="text.secondary" display="block">
                        Remote Status: <strong>{conflict.externalStatus || 'N/A'}</strong>
                      </Typography>
                      <Typography variant="caption" color="text.secondary" display="block">
                        Remote Assignee: {conflict.externalAssigneeEmail || 'Unassigned'}
                      </Typography>
                    </Paper>
                  </Grid>
                </Grid>

                {/* Action Buttons: Merge, Keep Both, Skip */}
                <Box sx={{ display: 'flex', justifyContent: 'flex-end', gap: 1.5, flexWrap: 'wrap' }}>
                  <Button
                    size="small"
                    variant="contained"
                    color="primary"
                    startIcon={<CallMergeIcon />}
                    onClick={() => handleResolve(conflict.id, 'Merge')}
                    disabled={isProcessing || !canMergeConflict(conflict)}
                    title={
                      !canMergeConflict(conflict)
                        ? 'Cannot merge because no local CRM work item is linked.'
                        : undefined
                    }
                    sx={{ fontWeight: 600 }}
                  >
                    Merge (Link External ID)
                  </Button>

                  <Button
                    size="small"
                    variant="outlined"
                    color="secondary"
                    startIcon={<ContentCopyIcon />}
                    onClick={() => handleResolve(conflict.id, 'KeepBoth')}
                    disabled={isProcessing}
                    sx={{ fontWeight: 600 }}
                  >
                    Keep Both (Create New CRM Item)
                  </Button>

                  <Button
                    size="small"
                    variant="text"
                    color="inherit"
                    startIcon={<SkipNextIcon />}
                    onClick={() => handleResolve(conflict.id, 'Skip')}
                    disabled={isProcessing}
                    sx={{ fontWeight: 600 }}
                  >
                    Skip (Ignore Remote Item)
                  </Button>
                </Box>
              </Paper>
            );
          })}
        </Box>
      )}
    </Paper>
  );
};
