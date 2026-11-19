import React, { useState } from 'react';
import Box from '@mui/material/Box';
import Typography from '@mui/material/Typography';
import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableContainer from '@mui/material/TableContainer';
import TableHead from '@mui/material/TableHead';
import TableRow from '@mui/material/TableRow';
import Paper from '@mui/material/Paper';
import Rating from '@mui/material/Rating';
import TextField from '@mui/material/TextField';
import Button from '@mui/material/Button';
import Chip from '@mui/material/Chip';
import IconButton from '@mui/material/IconButton';
import DeleteIcon from '@mui/icons-material/Delete';
import AddIcon from '@mui/icons-material/Add';
import Alert from '@mui/material/Alert';
import { EmployeeSkillDto, UpsertSkillRequestDto } from '../../types';
import { employeesApi } from '../../services/employeesApi';

interface SkillsMatrixEditorProps {
  employeeId: string;
  skills: EmployeeSkillDto[];
  onSkillsChanged: () => void;
  canEdit?: boolean;
}

export const SkillsMatrixEditor: React.FC<SkillsMatrixEditorProps> = ({
  employeeId,
  skills,
  onSkillsChanged,
  canEdit = true,
}) => {
  const [skillName, setSkillName] = useState('');
  const [category, setCategory] = useState('Frontend');
  const [proficiencyLevel, setProficiencyLevel] = useState<number>(3);
  const [yearsOfExperience, setYearsOfExperience] = useState<string>('2.0');
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const handleAddSkill = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!skillName.trim()) {
      setError('Skill name is required.');
      return;
    }

    setIsSubmitting(true);
    setError(null);

    try {
      const dto: UpsertSkillRequestDto = {
        skillName: skillName.trim(),
        category: category.trim() || 'General',
        proficiencyLevel,
        yearsOfExperience: parseFloat(yearsOfExperience) || 0,
      };

      await employeesApi.upsertSkill(employeeId, dto);
      setSkillName('');
      setYearsOfExperience('2.0');
      onSkillsChanged();
    } catch (err: any) {
      setError(err?.response?.data?.detail || err?.message || 'Failed to save skill.');
    } finally {
      setIsSubmitting(false);
    }
  };

  const handleRatingChange = async (skill: EmployeeSkillDto, newLevel: number) => {
    if (!canEdit) return;
    try {
      await employeesApi.upsertSkill(employeeId, {
        skillName: skill.skillName,
        category: skill.category,
        proficiencyLevel: newLevel,
        yearsOfExperience: skill.yearsOfExperience,
      });
      onSkillsChanged();
    } catch (err) {
      console.error('Failed to update skill rating:', err);
    }
  };

  const handleDeleteSkill = async (skillToDelete: string) => {
    if (!canEdit) return;
    try {
      await employeesApi.deleteSkill(employeeId, skillToDelete);
      onSkillsChanged();
    } catch (err) {
      console.error('Failed to delete skill:', err);
    }
  };

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
      {error && <Alert severity="error">{error}</Alert>}

      {/* Add / Upsert Skill Form */}
      {canEdit && (
        <Paper variant="outlined" sx={{ p: 2, borderRadius: 2, bgcolor: 'action.hover' }}>
          <Typography variant="subtitle2" sx={{ fontWeight: 700, mb: 1.5 }}>
            Add or Update Skill
          </Typography>
          <form onSubmit={handleAddSkill}>
            <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 2, alignItems: 'center' }}>
              <TextField
                size="small"
                label="Skill Name"
                placeholder="e.g. React 19, C#, Docker..."
                value={skillName}
                onChange={(e) => setSkillName(e.target.value)}
                required
                sx={{ flexGrow: 1, minWidth: 180 }}
              />

              <TextField
                size="small"
                label="Category"
                placeholder="e.g. Frontend, Backend..."
                value={category}
                onChange={(e) => setCategory(e.target.value)}
                sx={{ width: 140 }}
              />

              <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
                <Typography variant="body2" sx={{ fontWeight: 600 }}>
                  Proficiency:
                </Typography>
                <Rating
                  value={proficiencyLevel}
                  max={5}
                  onChange={(_e, val) => setProficiencyLevel(val || 1)}
                />
              </Box>

              <TextField
                size="small"
                type="number"
                label="Years Exp"
                value={yearsOfExperience}
                onChange={(e) => setYearsOfExperience(e.target.value)}
                inputProps={{ min: 0, step: 0.5 }}
                sx={{ width: 100 }}
              />

              <Button
                type="submit"
                variant="contained"
                startIcon={<AddIcon />}
                disabled={isSubmitting}
                sx={{ fontWeight: 600, whiteSpace: 'nowrap' }}
              >
                Save Skill
              </Button>
            </Box>
          </form>
        </Paper>
      )}

      {/* Current Skills Table */}
      <TableContainer component={Paper} variant="outlined" sx={{ borderRadius: 2 }}>
        <Table size="small">
          <TableHead sx={{ bgcolor: 'action.hover' }}>
            <TableRow>
              <TableCell sx={{ fontWeight: 700 }}>Skill Name</TableCell>
              <TableCell sx={{ fontWeight: 700 }}>Category</TableCell>
              <TableCell sx={{ fontWeight: 700 }}>Proficiency Level (1–5)</TableCell>
              <TableCell sx={{ fontWeight: 700 }}>Experience</TableCell>
              {canEdit && (
                <TableCell align="right" sx={{ fontWeight: 700 }}>
                  Actions
                </TableCell>
              )}
            </TableRow>
          </TableHead>
          <TableBody>
            {skills.length === 0 ? (
              <TableRow>
                <TableCell colSpan={canEdit ? 5 : 4} align="center" sx={{ py: 3 }}>
                  <Typography variant="body2" color="text.secondary">
                    No skills recorded in matrix yet.
                  </Typography>
                </TableCell>
              </TableRow>
            ) : (
              skills.map((skill) => (
                <TableRow key={skill.id} hover>
                  <TableCell sx={{ fontWeight: 600 }}>{skill.skillName}</TableCell>
                  <TableCell>
                    <Chip label={skill.category} size="small" variant="outlined" />
                  </TableCell>
                  <TableCell>
                    <Rating
                      value={skill.proficiencyLevel}
                      max={5}
                      onChange={(_e, val) => val && handleRatingChange(skill, val)}
                      disabled={!canEdit}
                      size="small"
                    />
                  </TableCell>
                  <TableCell>{skill.yearsOfExperience} years</TableCell>
                  {canEdit && (
                    <TableCell align="right">
                      <IconButton
                        size="small"
                        color="error"
                        onClick={() => handleDeleteSkill(skill.skillName)}
                      >
                        <DeleteIcon fontSize="small" />
                      </IconButton>
                    </TableCell>
                  )}
                </TableRow>
              ))
            )}
          </TableBody>
        </Table>
      </TableContainer>
    </Box>
  );
};
