import React from 'react';
import Box from '@mui/material/Box';
import Typography from '@mui/material/Typography';
import { useProject } from '../contexts/ProjectContext';
import { ExternalConfigCard } from '../components/sync/ExternalConfigCard';
import { ChatWebhookConfigCard } from '../components/sync/ChatWebhookConfigCard';
import { SyncLogFeed } from '../components/sync/SyncLogFeed';
import { ConflictResolver } from '../components/sync/ConflictResolver';

export const ExternalSyncPage: React.FC = () => {
  const { activeProject } = useProject();

  if (!activeProject) {
    return (
      <Box sx={{ p: 4, textAlign: 'center' }}>
        <Typography variant="h6" color="text.secondary">
          Please select a project to configure external issue tracker sync.
        </Typography>
      </Box>
    );
  }

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', gap: 3 }}>
      <Box>
        <Typography variant="h5" sx={{ fontWeight: 700 }}>
          External Synchronization & Conflict Deduplication
        </Typography>
        <Typography variant="body2" color="text.secondary">
          Two-way Azure DevOps, Jira, and GitHub sync with SHA-256 loop prevention and token similarity deduplication.
        </Typography>
      </Box>

      {/* Provider Configuration */}
      <ExternalConfigCard projectId={activeProject.id} />

      {/* Slack & Microsoft Teams Alerts */}
      <ChatWebhookConfigCard projectId={activeProject.id} />

      {/* Sync Operations & Live Feed */}
      <SyncLogFeed projectId={activeProject.id} />

      {/* Conflict & Deduplication Resolver */}
      <ConflictResolver projectId={activeProject.id} />
    </Box>
  );
};
