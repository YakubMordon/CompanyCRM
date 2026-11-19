import React, { useState, useEffect } from 'react';
import Paper from '@mui/material/Paper';
import Box from '@mui/material/Box';
import Typography from '@mui/material/Typography';
import TextField from '@mui/material/TextField';
import Button from '@mui/material/Button';
import FormControlLabel from '@mui/material/FormControlLabel';
import Switch from '@mui/material/Switch';
import Alert from '@mui/material/Alert';
import CircularProgress from '@mui/material/CircularProgress';
import SettingsIcon from '@mui/icons-material/Settings';
import CloudQueueIcon from '@mui/icons-material/CloudQueue';
import CheckCircleIcon from '@mui/icons-material/CheckCircle';
import { ProjectExternalConfigDto, UpdateProjectExternalConfigDto } from '../../types';
import { projectsApi } from '../../services/projectsApi';

interface ExternalConfigCardProps {
  projectId: string;
}

export const ExternalConfigCard: React.FC<ExternalConfigCardProps> = ({ projectId }) => {
  const [config, setConfig] = useState<ProjectExternalConfigDto | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [orgUrl, setOrgUrl] = useState('');
  const [projectName, setProjectName] = useState('');
  const [pat, setPat] = useState('');
  const [isSimulatedMode, setIsSimulatedMode] = useState(true);
  const [isSyncEnabled, setIsSyncEnabled] = useState(true);
  const [isSaving, setIsSaving] = useState(false);
  const [saveSuccess, setSaveSuccess] = useState<string | null>(null);
  const [saveError, setSaveError] = useState<string | null>(null);

  useEffect(() => {
    let isMounted = true;
    setIsLoading(true);
    projectsApi
      .getExternalConfig(projectId)
      .then((cfg) => {
        if (!isMounted) return;
        setConfig(cfg);
        setOrgUrl(cfg.externalOrgUrl || 'https://dev.azure.com/contoso-crm');
        setProjectName(cfg.externalProjectName || 'CompanyCRM');
        setPat('');
        setIsSimulatedMode(cfg.isSimulatedMode);
        setIsSyncEnabled(cfg.isSyncEnabled);
      })
      .catch((err) => {
        if (!isMounted) return;
        console.error('Failed to load external config:', err);
      })
      .finally(() => {
        if (isMounted) setIsLoading(false);
      });
    return () => {
      isMounted = false;
    };
  }, [projectId]);

  const handleSave = async (e: React.FormEvent) => {
    e.preventDefault();
    setIsSaving(true);
    setSaveSuccess(null);
    setSaveError(null);

    try {
      const dto: UpdateProjectExternalConfigDto = {
        externalSource: 'AzureDevOps',
        externalOrgUrl: orgUrl.trim() || null,
        externalProjectName: projectName.trim() || null,
        externalPat: pat.trim() || null,
        personalAccessToken: pat.trim() || null,
        isSimulatedMode,
        isSyncEnabled,
      };

      const updated = await projectsApi.updateExternalConfig(projectId, dto);
      setConfig(updated);
      setSaveSuccess('Azure DevOps configuration saved successfully.');
    } catch (err: any) {
      setSaveError(err?.response?.data?.detail || err?.message || 'Failed to save configuration.');
    } finally {
      setIsSaving(false);
    }
  };

  if (isLoading) {
    return (
      <Paper variant="outlined" sx={{ p: 3, borderRadius: 2, textAlign: 'center' }}>
        <CircularProgress size={28} />
      </Paper>
    );
  }

  return (
    <Paper variant="outlined" sx={{ p: 3, borderRadius: 2 }}>
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, mb: 2 }}>
        <CloudQueueIcon color="primary" />
        <Typography variant="h6" sx={{ fontWeight: 700 }}>
          Azure DevOps Integration Provider
        </Typography>
      </Box>

      {saveSuccess && (
        <Alert severity="success" sx={{ mb: 2 }}>
          {saveSuccess}
        </Alert>
      )}
      {saveError && (
        <Alert severity="error" sx={{ mb: 2 }}>
          {saveError}
        </Alert>
      )}

      <form onSubmit={handleSave}>
        <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
          <Box sx={{ p: 2, bgcolor: 'action.hover', borderRadius: 2 }}>
            <FormControlLabel
              control={
                <Switch
                  checked={isSimulatedMode}
                  onChange={(e) => setIsSimulatedMode(e.target.checked)}
                  color="warning"
                />
              }
              label={
                <Box>
                  <Typography variant="subtitle2" sx={{ fontWeight: 700 }}>
                    Simulated Sandbox Mode ({isSimulatedMode ? 'ACTIVE' : 'LIVE PAT'})
                  </Typography>
                  <Typography variant="caption" color="text.secondary">
                    Runs deterministic offline Azure DevOps sync with realistic work items, mock
                    webhooks, and conflict deduplication locally without needing live credentials.
                  </Typography>
                </Box>
              }
            />
          </Box>

          <Box sx={{ display: 'flex', gap: 2, flexWrap: 'wrap' }}>
            <TextField
              size="small"
              label="Azure DevOps Organization URL"
              value={orgUrl}
              onChange={(e) => setOrgUrl(e.target.value)}
              sx={{ flexGrow: 1, minWidth: 260 }}
              required
            />

            <TextField
              size="small"
              label="Project Name"
              value={projectName}
              onChange={(e) => setProjectName(e.target.value)}
              sx={{ flexGrow: 1, minWidth: 200 }}
              required
            />
          </Box>

          <Box sx={{ display: 'flex', gap: 2, flexWrap: 'wrap', alignItems: 'center' }}>
            <TextField
              size="small"
              type="password"
              label={
                config?.hasPatConfigured || config?.personalAccessTokenMasked
                  ? `PAT (Configured: ${config.personalAccessTokenMasked || '***'})`
                  : 'Personal Access Token (PAT)'
              }
              placeholder="Enter new token to update..."
              value={pat}
              onChange={(e) => setPat(e.target.value)}
              sx={{ flexGrow: 1, minWidth: 260 }}
            />

            <FormControlLabel
              control={
                <Switch
                  checked={isSyncEnabled}
                  onChange={(e) => setIsSyncEnabled(e.target.checked)}
                  color="primary"
                />
              }
              label={<Typography variant="body2">Enable Two-Way Sync</Typography>}
            />
          </Box>

          <Box sx={{ display: 'flex', justifyContent: 'flex-end', mt: 1 }}>
            <Button
              type="submit"
              variant="contained"
              startIcon={<SettingsIcon />}
              disabled={isSaving}
              sx={{ fontWeight: 600 }}
            >
              {isSaving ? 'Saving...' : 'Save Configuration'}
            </Button>
          </Box>
        </Box>
      </form>
    </Paper>
  );
};
