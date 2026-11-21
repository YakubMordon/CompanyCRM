import React, { useState, useEffect } from 'react';
import Paper from '@mui/material/Paper';
import Box from '@mui/material/Box';
import Typography from '@mui/material/Typography';
import CircularProgress from '@mui/material/CircularProgress';
import {
  BarChart,
  Bar,
  XAxis,
  YAxis,
  CartesianGrid,
  Tooltip,
  Legend,
  ResponsiveContainer,
} from 'recharts';
import { HoursComparisonDto } from '../../types';
import { managementApi } from '../../services/managementApi';

interface HoursComparisonChartProps {
  projectId?: string;
}

export const HoursComparisonChart: React.FC<HoursComparisonChartProps> = ({ projectId }) => {
  const [data, setData] = useState<HoursComparisonDto[]>([]);
  const [isLoading, setIsLoading] = useState(true);

  useEffect(() => {
    setIsLoading(true);
    managementApi
      .getHoursComparison(projectId || undefined)
      .then(setData)
      .catch(console.error)
      .finally(() => setIsLoading(false));
  }, [projectId]);

  if (isLoading) {
    return (
      <Paper variant="outlined" sx={{ p: 3, borderRadius: 2, height: 320, display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
        <CircularProgress size={28} />
      </Paper>
    );
  }

  return (
    <Paper variant="outlined" sx={{ p: 2.5, borderRadius: 2 }}>
      <Typography variant="subtitle1" sx={{ fontWeight: 700, mb: 1 }}>
        Logged vs. Estimated Hours
      </Typography>
      <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mb: 2 }}>
        Estimated scope compared with actual billable and non-billable hours logged.
      </Typography>

      <Box sx={{ width: '100%', height: 260 }}>
        <ResponsiveContainer width="100%" height="100%">
          <BarChart data={data.slice(0, 8)} margin={{ top: 10, right: 20, left: 0, bottom: 5 }}>
            <CartesianGrid strokeDasharray="3 3" vertical={false} />
            <XAxis dataKey="workItemCode" />
            <YAxis />
            <Tooltip />
            <Legend />
            <Bar dataKey="estimatedHours" name="Estimated (h)" fill="#90caf9" radius={[4, 4, 0, 0]} />
            <Bar dataKey="billableHours" name="Billable (h)" stackId="a" fill="#2e7d32" />
            <Bar dataKey="nonBillableHours" name="Non-Billable (h)" stackId="a" fill="#ed6c02" radius={[4, 4, 0, 0]} />
          </BarChart>
        </ResponsiveContainer>
      </Box>
    </Paper>
  );
};
