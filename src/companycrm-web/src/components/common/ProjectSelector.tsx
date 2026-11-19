import React from 'react';
import Box from '@mui/material/Box';
import MenuItem from '@mui/material/MenuItem';
import FormControl from '@mui/material/FormControl';
import Select, { SelectChangeEvent } from '@mui/material/Select';
import Typography from '@mui/material/Typography';
import Chip from '@mui/material/Chip';
import FolderIcon from '@mui/icons-material/Folder';
import { useProject } from '../../contexts/ProjectContext';

export const ProjectSelector: React.FC = () => {
  const { projects, activeProject, setActiveProject, isLoading } = useProject();

  const handleChange = (event: SelectChangeEvent<string>) => {
    const selectedId = event.target.value;
    const found = projects.find((p) => p.id === selectedId) || null;
    setActiveProject(found);
  };

  if (isLoading || projects.length === 0) {
    return (
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
        <Chip label="Loading projects..." size="small" variant="outlined" />
      </Box>
    );
  }

  return (
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
  );
};
