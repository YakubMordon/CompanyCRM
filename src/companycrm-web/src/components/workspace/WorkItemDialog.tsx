import React, { useState, useEffect } from 'react';
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
import Tooltip from '@mui/material/Tooltip';
import InfoOutlinedIcon from '@mui/icons-material/InfoOutlined';
import InputAdornment from '@mui/material/InputAdornment';
import Box from '@mui/material/Box';
import {
  CreateWorkItemDto,
  ProjectMemberDto,
  WorkItemDto,
  WorkItemPriority,
  WorkItemType,
} from '../../types';
import { projectsApi } from '../../services/projectsApi';
import { FormDialog } from '../common/FormDialog';
import { workItemSchema, LIMITS } from '../../validation/schemas';
import { getApiErrorMessage } from '../../validation/errorMessage';

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
  const [fieldErrors, setFieldErrors] = useState<{
    title?: string;
    description?: string;
    sprintName?: string;
    estimatedHours?: string;
    storyPoints?: string;
  }>({});

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
    setFieldErrors({});
  }, [itemToEdit, open]);

  const validateFields = () => {
    const hoursNum = estimatedHours.trim() ? parseFloat(estimatedHours) : undefined;
    const pointsNum = storyPoints.trim() ? parseInt(storyPoints, 10) : undefined;

    const result = workItemSchema.safeParse({
      title,
      description,
      sprintName,
      estimatedHours: hoursNum,
      storyPoints: pointsNum,
    });

    if (!result.success) {
      const errs: typeof fieldErrors = {};
      for (const issue of result.error.issues) {
        const field = issue.path[0] as keyof typeof fieldErrors;
        if (field && !errs[field]) {
          errs[field] = issue.message;
        }
      }
      setFieldErrors(errs);
      return false;
    }

    setFieldErrors({});
    return true;
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!validateFields()) {
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
        estimatedHours: estimatedHours.trim() ? parseFloat(estimatedHours) : null,
        storyPoints: storyPoints.trim() ? parseInt(storyPoints, 10) : null,
        sprintName: sprintName.trim() || null,
      };

      await onSubmit(dto, itemToEdit?.id);
      onClose();
    } catch (err: any) {
      setError(getApiErrorMessage(err, 'Failed to save work item.'));
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <FormDialog open={open} onClose={onClose} maxWidth="md" fullWidth>
      <form onSubmit={handleSubmit}>
        <DialogTitle sx={{ fontWeight: 700 }}>
          {itemToEdit ? `Edit Work Item: ${itemToEdit.code}` : 'Create New Work Item'}
        </DialogTitle>
        <DialogContent dividers sx={{ pt: 3 }}>
          {error && (
            <Alert severity="error" sx={{ mb: 2 }}>
              {error}
            </Alert>
          )}

          <Grid container spacing={2.5}>
            <Grid item xs={12}>
              <TextField
                fullWidth
                label="Title"
                value={title}
                onChange={(e) => {
                  setTitle(e.target.value);
                  if (fieldErrors.title) validateFields();
                }}
                error={Boolean(fieldErrors.title)}
                helperText={fieldErrors.title || `${title.length}/${LIMITS.workItemTitleMax}`}
                inputProps={{ maxLength: LIMITS.workItemTitleMax }}
                required
                autoFocus
              />
            </Grid>

            <Grid item xs={12}>
              <TextField
                fullWidth
                label="Description"
                value={description}
                onChange={(e) => {
                  setDescription(e.target.value);
                  if (fieldErrors.description) validateFields();
                }}
                error={Boolean(fieldErrors.description)}
                helperText={fieldErrors.description || `${description.length}/${LIMITS.workItemDescriptionMax}`}
                inputProps={{ maxLength: LIMITS.workItemDescriptionMax }}
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
                onChange={(e) => {
                  setSprintName(e.target.value);
                  if (fieldErrors.sprintName) validateFields();
                }}
                error={Boolean(fieldErrors.sprintName)}
                helperText={fieldErrors.sprintName || `${sprintName.length}/${LIMITS.sprintNameMax}`}
                inputProps={{ maxLength: LIMITS.sprintNameMax }}
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
                onChange={(e) => {
                  setEstimatedHours(e.target.value);
                  if (fieldErrors.estimatedHours) validateFields();
                }}
                error={Boolean(fieldErrors.estimatedHours)}
                helperText={fieldErrors.estimatedHours}
                inputProps={{ min: LIMITS.estimatedHoursMin, max: LIMITS.estimatedHoursMax, step: 0.5 }}
                InputProps={{
                  endAdornment: (
                    <InputAdornment position="end">
                      <Tooltip title="Expected total effort in hours (0 - 9999.99)">
                        <InfoOutlinedIcon fontSize="small" sx={{ color: 'text.secondary', cursor: 'help' }} />
                      </Tooltip>
                    </InputAdornment>
                  ),
                }}
              />
            </Grid>

            <Grid item xs={12} sm={6}>
              <TextField
                fullWidth
                size="small"
                type="number"
                label="Story Points"
                value={storyPoints}
                onChange={(e) => {
                  setStoryPoints(e.target.value);
                  if (fieldErrors.storyPoints) validateFields();
                }}
                error={Boolean(fieldErrors.storyPoints)}
                helperText={fieldErrors.storyPoints}
                inputProps={{ min: LIMITS.storyPointsMin, max: LIMITS.storyPointsMax, step: 1 }}
                InputProps={{
                  endAdornment: (
                    <InputAdornment position="end">
                      <Tooltip title="Agile velocity complexity points (0 - 1000)">
                        <InfoOutlinedIcon fontSize="small" sx={{ color: 'text.secondary', cursor: 'help' }} />
                      </Tooltip>
                    </InputAdornment>
                  ),
                }}
              />
            </Grid>
          </Grid>
        </DialogContent>
        <DialogActions sx={{ px: 3, py: 2 }}>
          <Tooltip title="Cancel changes">
            <Button onClick={onClose} disabled={isSubmitting}>
              Cancel
            </Button>
          </Tooltip>
          <Tooltip title={isSubmitting ? 'Saving...' : itemToEdit ? 'Save updated work item' : 'Create new work item'}>
            <span>
              <Button type="submit" variant="contained" disabled={isSubmitting}>
                {isSubmitting ? 'Saving...' : itemToEdit ? 'Save Changes' : 'Create Work Item'}
              </Button>
            </span>
          </Tooltip>
        </DialogActions>
      </form>
    </FormDialog>
  );
};

export default WorkItemDialog;
