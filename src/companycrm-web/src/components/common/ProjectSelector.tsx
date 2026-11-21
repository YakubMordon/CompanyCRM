import React, { useState } from 'react';
import Box from '@mui/material/Box';
import MenuItem from '@mui/material/MenuItem';
import FormControl from '@mui/material/FormControl';
import Select, { SelectChangeEvent } from '@mui/material/Select';
import Typography from '@mui/material/Typography';
import Chip from '@mui/material/Chip';
import FolderIcon from '@mui/icons-material/Folder';
import AddIcon from '@mui/icons-material/Add';
import IconButton from '@mui/material/IconButton';
import Tooltip from '@mui/material/Tooltip';
import DialogTitle from '@mui/material/DialogTitle';
import DialogContent from '@mui/material/DialogContent';
import DialogActions from '@mui/material/DialogActions';
import TextField from '@mui/material/TextField';
import Button from '@mui/material/Button';
import Alert from '@mui/material/Alert';
import { useProject } from '../../contexts/ProjectContext';
import { useAuth } from '../../contexts/AuthContext';
import { projectsApi } from '../../services/projectsApi';
import { FormDialog } from './FormDialog';
import { TruncatedText } from './TruncatedText';
import { projectSchema, LIMITS } from '../../validation/schemas';
import { getApiErrorMessage } from '../../validation/errorMessage';

export const ProjectSelector: React.FC = () => {
  const { projects, activeProject, setActiveProject, refreshProjects, isLoading } = useProject();
  const { role } = useAuth();
  const canManageProjects = role === 'HigherManagement' || role === 'TeamLead';

  const [isCreateOpen, setIsCreateOpen] = useState(false);
  const [newCode, setNewCode] = useState('');
  const [newName, setNewName] = useState('');
  const [newDescription, setNewDescription] = useState('');
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [createError, setCreateError] = useState<string | null>(null);

  // Field validation errors
  const [fieldErrors, setFieldErrors] = useState<{ code?: string; name?: string; description?: string }>({});

  const handleChange = (event: SelectChangeEvent<string>) => {
    const selectedId = event.target.value;
    const found = projects.find((p) => p.id === selectedId) || null;
    setActiveProject(found);
  };

  const validateInputs = () => {
    const result = projectSchema.safeParse({
      code: newCode,
      name: newName,
      description: newDescription,
    });

    if (!result.success) {
      const errors: { code?: string; name?: string; description?: string } = {};
      for (const issue of result.error.issues) {
        const field = issue.path[0] as 'code' | 'name' | 'description';
        if (field && !errors[field]) {
          errors[field] = issue.message;
        }
      }
      setFieldErrors(errors);
      return false;
    }

    setFieldErrors({});
    return true;
  };

  const handleCreateProject = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!validateInputs()) {
      return;
    }

    try {
      setIsSubmitting(true);
      setCreateError(null);
      const created = await projectsApi.createProject({
        code: newCode.trim().toUpperCase(),
        name: newName.trim(),
        description: newDescription.trim() || undefined,
      });
      await refreshProjects();
      setActiveProject(created);
      setIsCreateOpen(false);
      setNewCode('');
      setNewName('');
      setNewDescription('');
      setFieldErrors({});
    } catch (err: any) {
      setCreateError(getApiErrorMessage(err, 'Failed to create project.'));
    } finally {
      setIsSubmitting(false);
    }
  };

  if (isLoading || projects.length === 0) {
    return (
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
        <Chip label="Loading projects..." size="small" variant="outlined" />
      </Box>
    );
  }

  return (
    <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
      <Tooltip title={activeProject ? `Current Project: ${activeProject.name} (${activeProject.code})` : 'Select Active Project'}>
        <FormControl size="small" sx={{ width: { xs: 180, sm: 260, md: 300 } }}>
          <Select
            value={activeProject?.id || ''}
            onChange={handleChange}
            displayEmpty
            renderValue={(selectedId) => {
              const selectedProject = projects.find((p) => p.id === selectedId);
              if (!selectedProject) return <em>Select Project</em>;
              return (
                <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, minWidth: 0, width: '100%', overflow: 'hidden' }}>
                  <FolderIcon fontSize="small" color="primary" sx={{ flexShrink: 0 }} />
                  <Typography
                    variant="body2"
                    noWrap
                    sx={{
                      fontWeight: 600,
                      overflow: 'hidden',
                      textOverflow: 'ellipsis',
                      whiteSpace: 'nowrap',
                      flexGrow: 1,
                      minWidth: 0,
                    }}
                  >
                    {selectedProject.name}
                  </Typography>
                  <Chip
                    label={selectedProject.code}
                    size="small"
                    variant="outlined"
                    sx={{ height: 18, fontSize: '0.65rem', fontWeight: 600, flexShrink: 0 }}
                  />
                </Box>
              );
            }}
            MenuProps={{
              PaperProps: {
                sx: {
                  maxWidth: 420,
                  maxHeight: 380,
                },
              },
            }}
            sx={{
              borderRadius: 2,
              fontSize: '0.875rem',
              fontWeight: 500,
              '& .MuiSelect-select': {
                display: 'flex',
                alignItems: 'center',
                py: 0.75,
                minWidth: 0,
              },
            }}
          >
            {projects.map((project) => (
              <MenuItem key={project.id} value={project.id} sx={{ maxWidth: '100%' }}>
                <Tooltip
                  title={`${project.name} (${project.code})${project.description ? ` — ${project.description}` : ''}`}
                  placement="right"
                >
                  <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, width: '100%', minWidth: 0 }}>
                    <FolderIcon fontSize="small" color="primary" sx={{ flexShrink: 0 }} />
                    <TruncatedText
                      text={project.name}
                      variant="body2"
                      sx={{ fontWeight: 600, flexGrow: 1, minWidth: 0 }}
                    />
                    <Chip
                      label={project.code}
                      size="small"
                      variant="outlined"
                      sx={{ height: 18, fontSize: '0.65rem', fontWeight: 600, flexShrink: 0 }}
                    />
                  </Box>
                </Tooltip>
              </MenuItem>
            ))}
          </Select>
        </FormControl>
      </Tooltip>

      {canManageProjects && (
        <Tooltip title="Create New Project">
          <IconButton
            size="small"
            color="primary"
            onClick={() => {
              setCreateError(null);
              setFieldErrors({});
              setIsCreateOpen(true);
            }}
            sx={{ border: 1, borderColor: 'divider', borderRadius: 2 }}
          >
            <AddIcon fontSize="small" />
          </IconButton>
        </Tooltip>
      )}

      {/* New Project Dialog */}
      <FormDialog open={isCreateOpen} onClose={() => setIsCreateOpen(false)} maxWidth="xs" fullWidth>
        <form onSubmit={handleCreateProject}>
          <DialogTitle sx={{ fontWeight: 700 }}>Create New Project</DialogTitle>
          <DialogContent sx={{ display: 'flex', flexDirection: 'column', gap: 2.5, pt: 3 }}>
            {createError && <Alert severity="error">{createError}</Alert>}
            <TextField
              label="Project Code"
              placeholder="e.g. CRM, PRJ"
              value={newCode}
              onChange={(e) => {
                const val = e.target.value.toUpperCase();
                setNewCode(val);
                if (fieldErrors.code) validateInputs();
              }}
              error={Boolean(fieldErrors.code)}
              helperText={fieldErrors.code || `${newCode.length}/${LIMITS.projectCodeMax} (Alphanumeric only)`}
              inputProps={{ maxLength: LIMITS.projectCodeMax }}
              required
              fullWidth
              size="small"
              autoFocus
            />
            <TextField
              label="Project Name"
              placeholder="e.g. Enterprise CRM"
              value={newName}
              onChange={(e) => {
                setNewName(e.target.value);
                if (fieldErrors.name) validateInputs();
              }}
              error={Boolean(fieldErrors.name)}
              helperText={fieldErrors.name || `${newName.length}/${LIMITS.projectNameMax}`}
              inputProps={{ maxLength: LIMITS.projectNameMax }}
              required
              fullWidth
              size="small"
            />
            <TextField
              label="Description"
              placeholder="Short description..."
              value={newDescription}
              onChange={(e) => {
                setNewDescription(e.target.value);
                if (fieldErrors.description) validateInputs();
              }}
              error={Boolean(fieldErrors.description)}
              helperText={fieldErrors.description || `${newDescription.length}/${LIMITS.projectDescriptionMax}`}
              inputProps={{ maxLength: LIMITS.projectDescriptionMax }}
              multiline
              rows={3}
              fullWidth
              size="small"
            />
          </DialogContent>
          <DialogActions sx={{ p: 2, pt: 1 }}>
            <Tooltip title="Cancel and close dialog">
              <Button onClick={() => setIsCreateOpen(false)} disabled={isSubmitting}>
                Cancel
              </Button>
            </Tooltip>
            <Tooltip title={isSubmitting ? 'Creating project...' : 'Submit and create new project'}>
              <span>
                <Button type="submit" variant="contained" disabled={isSubmitting}>
                  {isSubmitting ? 'Creating...' : 'Create Project'}
                </Button>
              </span>
            </Tooltip>
          </DialogActions>
        </form>
      </FormDialog>
    </Box>
  );
};

export default ProjectSelector;
