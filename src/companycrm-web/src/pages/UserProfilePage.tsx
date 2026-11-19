import React, { useState, useEffect, useCallback } from 'react';
import { useSearchParams } from 'react-router-dom';
import Box from '@mui/material/Box';
import Typography from '@mui/material/Typography';
import Paper from '@mui/material/Paper';
import Grid from '@mui/material/Grid';
import Avatar from '@mui/material/Avatar';
import Chip from '@mui/material/Chip';
import TextField from '@mui/material/TextField';
import Button from '@mui/material/Button';
import Alert from '@mui/material/Alert';
import CircularProgress from '@mui/material/CircularProgress';
import SaveIcon from '@mui/icons-material/Save';
import { EmployeeProfileDto } from '../types';
import { employeesApi } from '../services/employeesApi';
import { useAuth } from '../contexts/AuthContext';
import { SkillsMatrixEditor } from '../components/employees/SkillsMatrixEditor';

export const UserProfilePage: React.FC = () => {
  const [searchParams] = useSearchParams();
  const { currentUser, role } = useAuth();

  const employeeId = searchParams.get('id') || currentUser?.id;
  const isSelf = employeeId === currentUser?.id;
  const canEdit = isSelf || role === 'HigherManagement' || role === 'TeamLead';

  const [profile, setProfile] = useState<EmployeeProfileDto | null>(null);
  const [bio, setBio] = useState('');
  const [isLoading, setIsLoading] = useState(true);
  const [isSavingBio, setIsSavingBio] = useState(false);
  const [bioFeedback, setBioFeedback] = useState<{ severity: 'success' | 'error'; message: string } | null>(
    null
  );

  const loadProfile = useCallback(async () => {
    if (!employeeId) return;
    setIsLoading(true);
    try {
      const data = await employeesApi.getEmployeeProfile(employeeId);
      setProfile(data);
      setBio(data.bio || '');
    } catch (err) {
      console.error('Failed to load employee profile:', err);
    } finally {
      setIsLoading(false);
    }
  }, [employeeId]);

  useEffect(() => {
    loadProfile();
  }, [loadProfile]);

  const handleSaveBio = async () => {
    if (!employeeId) return;
    setIsSavingBio(true);
    setBioFeedback(null);

    try {
      await employeesApi.updateProfile(employeeId, { bio: bio.trim() || null });
      setBioFeedback({ severity: 'success', message: 'Biography updated successfully.' });
      loadProfile();
    } catch (err: any) {
      setBioFeedback({
        severity: 'error',
        message: err?.response?.data?.detail || err?.message || 'Failed to update biography.',
      });
    } finally {
      setIsSavingBio(false);
    }
  };

  if (isLoading || !profile) {
    return (
      <Box sx={{ display: 'flex', justifyContent: 'center', p: 8 }}>
        <CircularProgress />
      </Box>
    );
  }

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', gap: 3 }}>
      {/* Profile Header Banner */}
      <Paper variant="outlined" sx={{ p: 3, borderRadius: 2 }}>
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 3, flexWrap: 'wrap' }}>
          <Avatar
            sx={{
              width: 80,
              height: 80,
              bgcolor: 'primary.main',
              fontSize: 32,
              fontWeight: 700,
            }}
          >
            {profile.fullName.charAt(0)}
          </Avatar>

          <Box sx={{ flexGrow: 1 }}>
            <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5, flexWrap: 'wrap' }}>
              <Typography variant="h5" sx={{ fontWeight: 700 }}>
                {profile.fullName}
              </Typography>
              <Chip
                label={profile.role}
                size="small"
                color={
                  profile.role === 'HigherManagement'
                    ? 'success'
                    : profile.role === 'TeamLead'
                    ? 'warning'
                    : 'primary'
                }
                sx={{ fontWeight: 600 }}
              />
              <Chip
                label={profile.seniorityRank}
                size="small"
                variant="outlined"
                sx={{ fontWeight: 600 }}
              />
            </Box>

            <Typography variant="body2" color="text.secondary" sx={{ mt: 0.5 }}>
              {profile.jobTitle} &bull; {profile.department} Department
            </Typography>
            <Typography variant="caption" color="text.secondary">
              {profile.email}
            </Typography>
          </Box>
        </Box>
      </Paper>

      <Grid container spacing={3}>
        {/* Left Column: Bio & Preferences */}
        <Grid item xs={12} md={5}>
          <Paper variant="outlined" sx={{ p: 3, borderRadius: 2, height: '100%' }}>
            <Typography variant="h6" sx={{ fontWeight: 700, mb: 2 }}>
              Professional Biography
            </Typography>

            {bioFeedback && (
              <Alert severity={bioFeedback.severity} sx={{ mb: 2 }}>
                {bioFeedback.message}
              </Alert>
            )}

            <TextField
              label="Bio / Professional Background"
              multiline
              rows={6}
              value={bio}
              onChange={(e) => setBio(e.target.value)}
              fullWidth
              disabled={!canEdit}
              placeholder="Describe experience, specialties, key accomplishments..."
              inputProps={{ maxLength: 2000 }}
              helperText={`${bio.length} / 2000 characters`}
              sx={{ mb: 2 }}
            />

            {canEdit && (
              <Box sx={{ display: 'flex', justifyContent: 'flex-end' }}>
                <Button
                  variant="contained"
                  startIcon={<SaveIcon />}
                  onClick={handleSaveBio}
                  disabled={isSavingBio}
                  sx={{ fontWeight: 600 }}
                >
                  {isSavingBio ? 'Saving...' : 'Save Biography'}
                </Button>
              </Box>
            )}
          </Paper>
        </Grid>

        {/* Right Column: Skills Matrix */}
        <Grid item xs={12} md={7}>
          <Paper variant="outlined" sx={{ p: 3, borderRadius: 2 }}>
            <Box sx={{ mb: 2 }}>
              <Typography variant="h6" sx={{ fontWeight: 700 }}>
                Interactive Skills Matrix
              </Typography>
              <Typography variant="caption" color="text.secondary">
                Technical competencies, framework proficiency (1–5 stars), and years of verified experience.
              </Typography>
            </Box>

            <SkillsMatrixEditor
              employeeId={profile.id}
              skills={profile.skills}
              onSkillsChanged={loadProfile}
              canEdit={canEdit}
            />
          </Paper>
        </Grid>
      </Grid>
    </Box>
  );
};
