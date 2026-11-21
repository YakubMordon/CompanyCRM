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
import Dialog from '@mui/material/Dialog';
import DialogTitle from '@mui/material/DialogTitle';
import DialogContent from '@mui/material/DialogContent';
import DialogActions from '@mui/material/DialogActions';
import TextField from '@mui/material/TextField';
import Button from '@mui/material/Button';
import Alert from '@mui/material/Alert';
import { useProject } from '../../contexts/ProjectContext';
import { useAuth } from '../../contexts/AuthContext';
import { projectsApi } from '../../services/projectsApi';

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

  const handleChange = (event: SelectChangeEvent<string>) => {
    const selectedId = event.target.value;
    const found = projects.find((p) => p.id === selectedId) || null;
    setActiveProject(found);
  };

  const handleCreateProject = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!newCode.trim() || !newName.trim()) {
      setCreateError('Project code and name are required.');
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
    } catch (err: any) {
      setCreateError(err?.response?.data?.detail || err?.message || 'Failed to create project.');
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
      <FormControl size="small" sx={{ minWidth: 200, maxWidth: 300 }}>
        <Select
          value={activeProject?.id || ''}
          onChange={handleChange}
          displayEmpty
          sx={{
            borderRadius: 2,
            fontSize: '0.875rem',
            fontWeight: 500,
            '& .MuiSelect-select': {
              display: 'flex',
              alignItems: 'center',
              gap: 1,
              py: 0.75,
            },
          }}
        >
          {projects.map((project) => (
            <MenuItem key={project.id} value={project.id}>
              <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
                <FolderIcon fontSize="small" color="primary" />
                <Typography variant="body2" sx={{ fontWeight: 600 }}>
                  {project.name}
                </Typography>
                <Chip
                  label={project.code}
                  size="small"
                  variant="outlined"
                  sx={{ height: 18, fontSize: '0.65rem', fontWeight: 600 }}
                />
              </Box>
            </MenuItem>
          ))}
        </Select>
      </FormControl>

      {canManageProjects && (
        <Tooltip title="Create New Project">
          <IconButton
            size="small"
            color="primary"
            onClick={() => setIsCreateOpen(true)}
            sx={{ border: 1, borderColor: 'divider', borderRadius: 2 }}
          >
            <AddIcon fontSize="small" />
          </IconButton>
        </Tooltip>
      )}

      {/* New Project Dialog */}
      <Dialog open={isCreateOpen} onClose={() => setIsCreateOpen(false)} maxWidth="xs" fullWidth>
        <form onSubmit={handleCreateProject}>
          <DialogTitle sx={{ fontWeight: 700 }}>Create New Project</DialogTitle>
          <DialogContent sx={{ display: 'flex', flexDirection: 'column', gap: 2, pt: 1 }}>
            {createError && <Alert severity="error">{createError}</Alert>}
            <TextField
              label="Project Code"
              placeholder="e.g. CRM, PRJ"
              value={newCode}
              onChange={(e) => setNewCode(e.target.value.toUpperCase())}
              required
              fullWidth
              size="small"
              autoFocus
            />
            <TextField
              label="Project Name"
              placeholder="e.g. Enterprise CRM"
              value={newName}
              onChange={(e) => setNewName(e.target.value)}
              required
              fullWidth
              size="small"
            />
            <TextField
              label="Description"
              placeholder="Short description..."
              value={newDescription}
              onChange={(e) => setNewDescription(e.target.value)}
              multiline
              rows={3}
              fullWidth
              size="small"
            />
          </DialogContent>
          <DialogActions sx={{ p: 2, pt: 0 }}>
            <Button onClick={() => setIsCreateOpen(false)} disabled={isSubmitting}>
              Cancel
            </Button>
            <Button type="submit" variant="contained" disabled={isSubmitting}>
              {isSubmitting ? 'Creating...' : 'Create Project'}
            </Button>
          </DialogActions>
        </form>
      </Dialog>
    </Box>
  );
};
