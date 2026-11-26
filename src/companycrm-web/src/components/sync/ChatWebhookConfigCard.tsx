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
import NotificationsActiveIcon from '@mui/icons-material/NotificationsActive';
import SendIcon from '@mui/icons-material/Send';
import SaveIcon from '@mui/icons-material/Save';
import { notificationsApi } from '../../services/notificationsApi';

interface ChatWebhookConfigCardProps {
  projectId: string;
}

export const ChatWebhookConfigCard: React.FC<ChatWebhookConfigCardProps> = ({ projectId }) => {
  const [slackUrl, setSlackUrl] = useState('');
  const [teamsUrl, setTeamsUrl] = useState('');
  const [isCutoffAlertsEnabled, setIsCutoffAlertsEnabled] = useState(true);
  const [isReviewAlertsEnabled, setIsReviewAlertsEnabled] = useState(true);
  const [isSimulatedMode, setIsSimulatedMode] = useState(true);

  const [isLoading, setIsLoading] = useState(true);
  const [isSaving, setIsSaving] = useState(false);
  const [isTesting, setIsTesting] = useState(false);
  const [feedback, setFeedback] = useState<{ severity: 'success' | 'error'; message: string } | null>(null);

  useEffect(() => {
    let isMounted = true;
    setIsLoading(true);
    notificationsApi
      .getWebhookConfig(projectId)
      .then((cfg) => {
        if (!isMounted) return;
        setSlackUrl(cfg.slackWebhookUrl || '');
        setTeamsUrl(cfg.teamsWebhookUrl || '');
        setIsCutoffAlertsEnabled(cfg.isCutoffAlertsEnabled);
        setIsReviewAlertsEnabled(cfg.isReviewAlertsEnabled);
        setIsSimulatedMode(cfg.isSimulatedMode);
      })
      .catch((err) => {
        if (!isMounted) return;
        console.error('Failed to load chat webhook config:', err);
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
    setFeedback(null);
    try {
      await notificationsApi.updateWebhookConfig({
        projectId,
        slackWebhookUrl: slackUrl.trim() || null,
        teamsWebhookUrl: teamsUrl.trim() || null,
        isCutoffAlertsEnabled,
        isReviewAlertsEnabled,
        isSimulatedMode,
      });
      setFeedback({ severity: 'success', message: 'Slack & Microsoft Teams webhook settings saved.' });
    } catch (err: any) {
      setFeedback({
        severity: 'error',
        message: err?.response?.data?.detail || err?.message || 'Failed to save webhook settings.',
      });
    } finally {
      setIsSaving(false);
    }
  };

  const handleTest = async () => {
    setIsTesting(true);
    setFeedback(null);
    try {
      const res = await notificationsApi.sendTestAlert(projectId);
      setFeedback({ severity: 'success', message: res.message });
    } catch (err: any) {
      setFeedback({
        severity: 'error',
        message: err?.response?.data?.detail || err?.message || 'Failed to send test alert.',
      });
    } finally {
      setIsTesting(false);
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
        <NotificationsActiveIcon color="primary" />
        <Typography variant="h6" sx={{ fontWeight: 700 }}>
          Slack &amp; Microsoft Teams Alerts
        </Typography>
      </Box>

      {feedback && (
        <Alert severity={feedback.severity} sx={{ mb: 2 }}>
          {feedback.message}
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
                    Simulated Sandbox Mode ({isSimulatedMode ? 'ACTIVE' : 'LIVE WEBHOOKS'})
                  </Typography>
                  <Typography variant="caption" color="text.secondary">
                    Logs formatted Slack Block Kit and Teams MessageCard payloads locally without external HTTP calls.
                  </Typography>
                </Box>
              }
            />
          </Box>

          <Box sx={{ display: 'flex', gap: 2, flexWrap: 'wrap' }}>
            <TextField
              size="small"
              label="Slack Incoming Webhook URL"
              placeholder="https://hooks.slack.com/services/..."
              value={slackUrl}
              onChange={(e) => setSlackUrl(e.target.value)}
              sx={{ flexGrow: 1, minWidth: 260 }}
            />
            <TextField
              size="small"
              label="Microsoft Teams Webhook URL"
              placeholder="https://outlook.office.com/webhook/..."
              value={teamsUrl}
              onChange={(e) => setTeamsUrl(e.target.value)}
              sx={{ flexGrow: 1, minWidth: 260 }}
            />
          </Box>

          <Box sx={{ display: 'flex', gap: 3, flexWrap: 'wrap' }}>
            <FormControlLabel
              control={
                <Switch
                  checked={isCutoffAlertsEnabled}
                  onChange={(e) => setIsCutoffAlertsEnabled(e.target.checked)}
                  color="primary"
                />
              }
              label={<Typography variant="body2">Daily Plan Cutoff Reminders</Typography>}
            />
            <FormControlLabel
              control={
                <Switch
                  checked={isReviewAlertsEnabled}
                  onChange={(e) => setIsReviewAlertsEnabled(e.target.checked)}
                  color="primary"
                />
              }
              label={<Typography variant="body2">Work Item Review Alerts</Typography>}
            />
          </Box>

          <Box sx={{ display: 'flex', justifyContent: 'flex-end', gap: 1.5, mt: 1 }}>
            <Button
              type="button"
              variant="outlined"
              startIcon={<SendIcon />}
              disabled={isTesting}
              onClick={handleTest}
            >
              {isTesting ? 'Sending...' : 'Send Test Alert'}
            </Button>
            <Button
              type="submit"
              variant="contained"
              startIcon={<SaveIcon />}
              disabled={isSaving}
              sx={{ fontWeight: 600 }}
            >
              {isSaving ? 'Saving...' : 'Save Alerts'}
            </Button>
          </Box>
        </Box>
      </form>
    </Paper>
  );
};
