import React, { useState } from 'react';
import Box from '@mui/material/Box';
import Typography from '@mui/material/Typography';
import Tabs from '@mui/material/Tabs';
import Tab from '@mui/material/Tab';
import Button from '@mui/material/Button';
import SettingsIcon from '@mui/icons-material/Settings';
import DateRangeIcon from '@mui/icons-material/DateRange';
import TodayIcon from '@mui/icons-material/Today';
import { useAuth } from '../contexts/AuthContext';
import { useProject } from '../contexts/ProjectContext';
import { WeeklyPlanningTab } from '../components/planning/WeeklyPlanningTab';
import { DailyPlanningTab } from '../components/planning/DailyPlanningTab';
import { CutoffConfigDialog } from '../components/planning/CutoffConfigDialog';

export const PlanningPage: React.FC = () => {
  const { role } = useAuth();
  const { activeProject } = useProject();
  const [activeTab, setActiveTab] = useState<number>(0);
  const [isCutoffDialogOpen, setIsCutoffDialogOpen] = useState<boolean>(false);

  const canManageCutoff = role === 'TeamLead' || role === 'HigherManagement';

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', gap: 3 }}>
      {/* Header Bar */}
      <Box
        sx={{
          display: 'flex',
          flexWrap: 'wrap',
          alignItems: 'center',
          justifyContent: 'space-between',
          gap: 2,
        }}
      >
        <Box>
          <Typography variant="h5" sx={{ fontWeight: 700 }}>
            Planning & Check-Ins
          </Typography>
          <Typography variant="body2" color="text.secondary">
            Weekly Monday-aligned commitments and daily standup plan check-ins.
          </Typography>
        </Box>

        {canManageCutoff && (
          <Button
            variant="outlined"
            startIcon={<SettingsIcon />}
            onClick={() => setIsCutoffDialogOpen(true)}
            sx={{ fontWeight: 600 }}
          >
            Cutoff Deadline Settings
          </Button>
        )}
      </Box>

      {/* Tabs */}
      <Box sx={{ borderBottom: 1, borderColor: 'divider' }}>
        <Tabs value={activeTab} onChange={(_e, val) => setActiveTab(val)}>
          <Tab
            icon={<DateRangeIcon />}
            iconPosition="start"
            label="Weekly Goal Planning"
            sx={{ fontWeight: 600 }}
          />
          <Tab
            icon={<TodayIcon />}
            iconPosition="start"
            label="Daily Plan Check-In"
            sx={{ fontWeight: 600 }}
          />
        </Tabs>
      </Box>

      {/* Tab Panels */}
      {activeTab === 0 && <WeeklyPlanningTab />}
      {activeTab === 1 && <DailyPlanningTab />}

      {/* Cutoff Config Dialog */}
      <CutoffConfigDialog
        open={isCutoffDialogOpen}
        onClose={() => setIsCutoffDialogOpen(false)}
        projectId={activeProject?.id}
        projectName={activeProject?.name}
      />
    </Box>
  );
};
