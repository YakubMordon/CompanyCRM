import React, { useState, useEffect } from 'react';
import Box from '@mui/material/Box';
import Typography from '@mui/material/Typography';
import Tabs from '@mui/material/Tabs';
import Tab from '@mui/material/Tab';
import Grid from '@mui/material/Grid';
import FormControl from '@mui/material/FormControl';
import InputLabel from '@mui/material/InputLabel';
import Select from '@mui/material/Select';
import MenuItem from '@mui/material/MenuItem';
import Tooltip from '@mui/material/Tooltip';
import MonetizationOnIcon from '@mui/icons-material/MonetizationOn';
import AssessmentIcon from '@mui/icons-material/Assessment';
import InsightsIcon from '@mui/icons-material/Insights';
import ReceiptLongIcon from '@mui/icons-material/ReceiptLong';
import { useAuth } from '../contexts/AuthContext';
import { useProject } from '../contexts/ProjectContext';
import { ForbiddenView } from '../components/management/ForbiddenView';
import { CompensationDataGrid } from '../components/management/CompensationDataGrid';
import { ProfitabilityTable } from '../components/management/ProfitabilityTable';
import { VelocityChart } from '../components/management/VelocityChart';
import { HoursComparisonChart } from '../components/management/HoursComparisonChart';
import { CompletionRatesChart } from '../components/management/CompletionRatesChart';
import { BurndownChart } from '../components/management/BurndownChart';
import { InvoicesTab } from '../components/management/InvoicesTab';

export const ManagementPortalPage: React.FC = () => {
  const { role } = useAuth();
  const { projects, activeProject } = useProject();
  const [activeTab, setActiveTab] = useState<number>(0);
  const [selectedProjectId, setSelectedProjectId] = useState<string>(activeProject?.id || 'ALL');

  useEffect(() => {
    setSelectedProjectId(activeProject?.id || 'ALL');
  }, [activeProject]);

  // Strict RBAC Guard: Only HigherManagement is authorized
  if (role !== 'HigherManagement') {
    return <ForbiddenView />;
  }

  const effectiveProjectId = selectedProjectId === 'ALL' ? undefined : selectedProjectId;

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
            Executive Management Portal & Analytics
          </Typography>
          <Typography variant="body2" color="text.secondary">
            Restricted executive dashboard for employee compensation, project margins, and delivery metrics.
          </Typography>
        </Box>

        <FormControl size="small" sx={{ width: 220 }}>
          <InputLabel>Analytics Scope</InputLabel>
          <Tooltip title="View metrics overall or per specific project">
            <Select
              value={selectedProjectId}
              label="Analytics Scope"
              onChange={(e) => setSelectedProjectId(e.target.value)}
            >
              <MenuItem value="ALL">
                <em>All Projects (Overall Company)</em>
              </MenuItem>
              {projects.map((p) => (
                <MenuItem key={p.id} value={p.id}>
                  {p.name} ({p.code})
                </MenuItem>
              ))}
            </Select>
          </Tooltip>
        </FormControl>
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
          <Tab
            icon={<ReceiptLongIcon />}
            iconPosition="start"
            label="Invoices & Billing"
            sx={{ fontWeight: 600 }}
          />
        </Tabs>
      </Box>

      {/* Tab Panels */}
      {activeTab === 0 && <CompensationDataGrid />}

      {activeTab === 1 && (
        <ProfitabilityTable projectId={effectiveProjectId} />
      )}

      {activeTab === 2 && (
        <Grid container spacing={3}>
          <Grid item xs={12} md={6}>
            <VelocityChart projectId={effectiveProjectId} />
          </Grid>
          <Grid item xs={12} md={6}>
            <HoursComparisonChart projectId={effectiveProjectId} />
          </Grid>
          <Grid item xs={12} md={6}>
            <CompletionRatesChart projectId={effectiveProjectId} />
          </Grid>
          <Grid item xs={12} md={6}>
            <BurndownChart projectId={effectiveProjectId} />
          </Grid>
        </Grid>
      )}

      {activeTab === 3 && <InvoicesTab projectId={effectiveProjectId} />}
    </Box>
  );
};
