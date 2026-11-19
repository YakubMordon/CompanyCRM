import React, { useState, useEffect } from 'react';
import Dialog from '@mui/material/Dialog';
import DialogTitle from '@mui/material/DialogTitle';
import DialogContent from '@mui/material/DialogContent';
import DialogActions from '@mui/material/DialogActions';
import Button from '@mui/material/Button';
import TextField from '@mui/material/TextField';
import Grid from '@mui/material/Grid';
import MenuItem from '@mui/material/MenuItem';
import FormControl from '@mui/material/FormControl';
import InputLabel from '@mui/material/InputLabel';
import Select from '@mui/material/Select';
import Typography from '@mui/material/Typography';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import {
  CreateWorkItemDto,
  ProjectMemberDto,
  WorkItemDto,
  WorkItemPriority,
  WorkItemType,
} from '../../types';
import { projectsApi } from '../../services/projectsApi';

interface WorkItemDialogProps {
  open: boolean;
  onClose: () => void;
  onSubmit: (dto: CreateWorkItemDto, id?: string) => Promise<void>;
  projectId: string;
  itemToEdit?: WorkItemDto | null;
}

export const WorkItemDialog: React.FC<WorkItemDialogProps> = ({
  open,
  onClose,
  onSubmit,
  projectId,
  itemToEdit,
}) => {
  const [title, setTitle] = useState('');
  const [description, setDescription] = useState('');
  const [type, setType] = useState<WorkItemType>('Task');
  const [priority, setPriority] = useState<WorkItemPriority>('Medium');
  const [assigneeId, setAssigneeId] = useState<string>('');
  const [reviewerId, setReviewerId] = useState<string>('');
  const [estimatedHours, setEstimatedHours] = useState<string>('');
  const [storyPoints, setStoryPoints] = useState<string>('');
  const [sprintName, setSprintName] = useState<string>('Sprint 1');

  const [members, setMembers] = useState<ProjectMemberDto[]>([]);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (projectId && open) {
      projectsApi
        .getProjectMembers(projectId)
        .then(setMembers)
        .catch(console.error);
    }
  }, [projectId, open]);

  useEffect(() => {
    if (itemToEdit) {
      setTitle(itemToEdit.title);
      setDescription(itemToEdit.description || '');
      setType(itemToEdit.type);
      setPriority(itemToEdit.priority);
      setAssigneeId(itemToEdit.assigneeId || '');
      setReviewerId(itemToEdit.reviewerId || '');
      setEstimatedHours(itemToEdit.estimatedHours != null ? itemToEdit.estimatedHours.toString() : '');
      setStoryPoints(itemToEdit.storyPoints != null ? itemToEdit.storyPoints.toString() : '');
      setSprintName(itemToEdit.sprintName || '');
    } else {
      setTitle('');
      setDescription('');
      setType('Task');
      setPriority('Medium');
      setAssigneeId('');
      setReviewerId('');
      setEstimatedHours('');
      setStoryPoints('');
      setSprintName('Sprint 1');
    }
    setError(null);
  }, [itemToEdit, open]);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!title.trim()) {
      setError('Title is required.');
      return;
    }

    setIsSubmitting(true);
    setError(null);

    try {
      const dto: CreateWorkItemDto = {
        projectId,
        title: title.trim(),
        description: description.trim() || null,
        type,
        priority,
        assigneeId: assigneeId || null,
        reviewerId: reviewerId || null,
        estimatedHours: estimatedHours ? parseFloat(estimatedHours) : null,
        storyPoints: storyPoints ? parseInt(storyPoints, 10) : null,
        sprintName: sprintName.trim() || null,
      };

      await onSubmit(dto, itemToEdit?.id);
      onClose();
    } catch (err: any) {
      const detail = err?.response?.data?.detail || err?.message || 'Failed to save work item.';
      setError(detail);
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <Dialog open={open} onClose={onClose} maxWidth="md" fullWidth>
      <form onSubmit={handleSubmit}>
        <DialogTitle sx={{ fontWeight: 700 }}>
          {itemToEdit ? `Edit Work Item: ${itemToEdit.code}` : 'Create New Work Item'}
        </DialogTitle>
        <DialogContent dividers>
          {error && (
            <Alert severity="error" sx={{ mb: 2 }}>
              {error}
            </Alert>
          )}

          <Grid container spacing={2}>
            <Grid item xs={12}>
              <TextField
                fullWidth
                label="Title"
                value={title}
                onChange={(e) => setTitle(e.target.value)}
                required
                autoFocus
              />
            </Grid>

            <Grid item xs={12}>
              <TextField
                fullWidth
                label="Description"
                value={description}
                onChange={(e) => setDescription(e.target.value)}
                multiline
                rows={3}
              />
            </Grid>

            <Grid item xs={12} sm={4}>
              <FormControl fullWidth size="small">
                <InputLabel>Type</InputLabel>
                <Select
                  value={type}
                  label="Type"
                  onChange={(e) => setType(e.target.value as WorkItemType)}
                >
                  <MenuItem value="Task">Task</MenuItem>
                  <MenuItem value="Bug">Bug</MenuItem>
                  <MenuItem value="Story">Story</MenuItem>
                </Select>
              </FormControl>
            </Grid>

            <Grid item xs={12} sm={4}>
              <FormControl fullWidth size="small">
                <InputLabel>Priority</InputLabel>
                <Select
                  value={priority}
                  label="Priority"
                  onChange={(e) => setPriority(e.target.value as WorkItemPriority)}
                >
                  <MenuItem value="Low">Low</MenuItem>
                  <MenuItem value="Medium">Medium</MenuItem>
                  <MenuItem value="High">High</MenuItem>
                  <MenuItem value="Critical">Critical</MenuItem>
                </Select>
              </FormControl>
            </Grid>

            <Grid item xs={12} sm={4}>
              <TextField
                fullWidth
                size="small"
                label="Sprint Name"
                value={sprintName}
                onChange={(e) => setSprintName(e.target.value)}
              />
            </Grid>

            <Grid item xs={12} sm={6}>
              <FormControl fullWidth size="small">
                <InputLabel>Assignee</InputLabel>
                <Select
                  value={assigneeId}
                  label="Assignee"
                  onChange={(e) => setAssigneeId(e.target.value)}
                >
                  <MenuItem value="">
                    <em>Unassigned</em>
                  </MenuItem>
                  {members.map((m) => (
                    <MenuItem key={m.employeeId} value={m.employeeId}>
                      {m.employeeName} ({m.role})
                    </MenuItem>
                  ))}
                </Select>
              </FormControl>
            </Grid>

            <Grid item xs={12} sm={6}>
              <FormControl fullWidth size="small">
                <InputLabel>Reviewer (Optional)</InputLabel>
                <Select
                  value={reviewerId}
                  label="Reviewer (Optional)"
                  onChange={(e) => setReviewerId(e.target.value)}
                >
                  <MenuItem value="">
                    <em>None (No review required)</em>
                  </MenuItem>
                  {members.map((m) => (
                    <MenuItem key={m.employeeId} value={m.employeeId}>
                      {m.employeeName} ({m.role})
                    </MenuItem>
                  ))}
                </Select>
              </FormControl>
              {reviewerId && (
                <Typography variant="caption" color="warning.main" sx={{ mt: 0.5, display: 'block', fontWeight: 500 }}>
                  Assigning a reviewer sets status to "Pending Review" and locks the item from Kanban movement until approved.
                </Typography>
              )}
            </Grid>

            <Grid item xs={12} sm={6}>
              <TextField
                fullWidth
                size="small"
                type="number"
                label="Estimated Hours"
                value={estimatedHours}
                onChange={(e) => setEstimatedHours(e.target.value)}
                inputProps={{ min: 0, step: 0.5 }}
              />
            </Grid>

            <Grid item xs={12} sm={6}>
              <TextField
                fullWidth
                size="small"
                type="number"
                label="Story Points"
                value={storyPoints}
                onChange={(e) => setStoryPoints(e.target.value)}
                inputProps={{ min: 0, step: 1 }}
              />
            </Grid>
          </Grid>
        </DialogContent>
        <DialogActions sx={{ px: 3, py: 2 }}>
          <Button onClick={onClose} disabled={isSubmitting}>
            Cancel
          </Button>
          <Button type="submit" variant="contained" disabled={isSubmitting}>
            {isSubmitting ? 'Saving...' : itemToEdit ? 'Save Changes' : 'Create Work Item'}
          </Button>
        </DialogActions>
      </form>
    </Dialog>
  );
};
