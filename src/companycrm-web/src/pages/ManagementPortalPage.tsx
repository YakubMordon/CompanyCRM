import React, { useState } from 'react';
import Box from '@mui/material/Box';
import Typography from '@mui/material/Typography';
import Tabs from '@mui/material/Tabs';
import Tab from '@mui/material/Tab';
import Grid from '@mui/material/Grid';
import MonetizationOnIcon from '@mui/icons-material/MonetizationOn';
import AssessmentIcon from '@mui/icons-material/Assessment';
import InsightsIcon from '@mui/icons-material/Insights';
import { useAuth } from '../contexts/AuthContext';
import { useProject } from '../contexts/ProjectContext';
import { ForbiddenView } from '../components/management/ForbiddenView';
import { CompensationDataGrid } from '../components/management/CompensationDataGrid';
import { ProfitabilityTable } from '../components/management/ProfitabilityTable';
import { VelocityChart } from '../components/management/VelocityChart';
import { HoursComparisonChart } from '../components/management/HoursComparisonChart';
import { CompletionRatesChart } from '../components/management/CompletionRatesChart';
import { BurndownChart } from '../components/management/BurndownChart';

export const ManagementPortalPage: React.FC = () => {
  const { role } = useAuth();
  const { activeProject } = useProject();
  const [activeTab, setActiveTab] = useState<number>(0);

  // Strict RBAC Guard: Only HigherManagement is authorized
  if (role !== 'HigherManagement') {
    return <ForbiddenView />;
  }

  const projectId = activeProject?.id || '';

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', gap: 3 }}>
      {/* Header Bar */}
      <Box>
        <Typography variant="h5" sx={{ fontWeight: 700 }}>
          Executive Management Portal & Analytics
        </Typography>
        <Typography variant="body2" color="text.secondary">
          Restricted executive dashboard for employee compensation, project margins, and delivery metrics.
        </Typography>
      </Box>

      {/* Tabs */}
      <Box sx={{ borderBottom: 1, borderColor: 'divider' }}>
        <Tabs value={activeTab} onChange={(_e, val) => setActiveTab(val)}>
          <Tab
            icon={<MonetizationOnIcon />}
            iconPosition="start"
            label="Compensation & Utilization"
            sx={{ fontWeight: 600 }}
          />
          <Tab
            icon={<AssessmentIcon />}
            iconPosition="start"
            label="Project Profitability"
            sx={{ fontWeight: 600 }}
          />
          <Tab
            icon={<InsightsIcon />}
            iconPosition="start"
            label="Analytics & Delivery Charts"
            sx={{ fontWeight: 600 }}
          />
        </Tabs>
      </Box>

      {/* Tab Panels */}
      {activeTab === 0 && <CompensationDataGrid />}

      {activeTab === 1 && (
        projectId ? (
          <ProfitabilityTable projectId={projectId} />
        ) : (
          <Typography variant="body2" color="text.secondary">
            Please select an active project from the top bar.
          </Typography>
        )
      )}

      {activeTab === 2 && (
        projectId ? (
          <Grid container spacing={3}>
            <Grid item xs={12} md={6}>
              <VelocityChart projectId={projectId} />
            </Grid>
            <Grid item xs={12} md={6}>
              <HoursComparisonChart projectId={projectId} />
            </Grid>
            <Grid item xs={12} md={6}>
              <CompletionRatesChart projectId={projectId} />
            </Grid>
            <Grid item xs={12} md={6}>
              <BurndownChart projectId={projectId} />
            </Grid>
          </Grid>
        ) : (
          <Typography variant="body2" color="text.secondary">
            Please select an active project from the top bar.
          </Typography>
        )
      )}
    </Box>
  );
};
