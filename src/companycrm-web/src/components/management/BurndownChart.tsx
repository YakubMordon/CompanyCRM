import React, { useState, useEffect } from 'react';
import Paper from '@mui/material/Paper';
import Box from '@mui/material/Box';
import Typography from '@mui/material/Typography';
import CircularProgress from '@mui/material/CircularProgress';
import {
  LineChart,
  Line,
  XAxis,
  YAxis,
  CartesianGrid,
  Tooltip,
  Legend,
  ResponsiveContainer,
} from 'recharts';
import { BurndownDto } from '../../types';
import { managementApi } from '../../services/managementApi';

interface BurndownChartProps {
  projectId: string;
}

export const BurndownChart: React.FC<BurndownChartProps> = ({ projectId }) => {
  const [data, setData] = useState<BurndownDto | null>(null);
  const [isLoading, setIsLoading] = useState(true);

  useEffect(() => {
    setIsLoading(true);
    managementApi
      .getBurndown(projectId, 'Sprint 1')
      .then(setData)
      .catch(console.error)
      .finally(() => setIsLoading(false));
  }, [projectId]);

  if (isLoading || !data) {
    return (
      <Paper variant="outlined" sx={{ p: 3, borderRadius: 2, height: 320, display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
        <CircularProgress size={28} />
      </Paper>
    );
  }

  const chartData = data.days.map((d) => ({
    day: `Day ${d.dayIndex}`,
    date: d.date,
    ideal: d.idealRemainingPoints,
    actual: d.actualRemainingPoints,
  }));

  return (
    <Paper variant="outlined" sx={{ p: 2.5, borderRadius: 2 }}>
      <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', mb: 1 }}>
        <Typography variant="subtitle1" sx={{ fontWeight: 700 }}>
          Sprint Burndown ({data.sprintName})
        </Typography>
        <Typography variant="caption" sx={{ fontWeight: 600 }}>
          Total Scope: {data.totalStoryPoints} SP
        </Typography>
      </Box>
      <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mb: 2 }}>
        Ideal remaining guideline vs. actual story points burned down.
      </Typography>

      <Box sx={{ width: '100%', height: 260 }}>
        <ResponsiveContainer width="100%" height="100%">
          <LineChart data={chartData} margin={{ top: 10, right: 20, left: 0, bottom: 5 }}>
            <CartesianGrid strokeDasharray="3 3" vertical={false} />
            <XAxis dataKey="day" />
            <YAxis />
            <Tooltip />
            <Legend />
            <Line
              type="monotone"
              dataKey="ideal"
              name="Ideal Burndown"
              stroke="#9e9e9e"
              strokeDasharray="5 5"
              strokeWidth={2}
              dot={false}
            />
            <Line
              type="monotone"
              dataKey="actual"
              name="Actual Remaining SP"
              stroke="#d32f2f"
              strokeWidth={2.5}
              activeDot={{ r: 6 }}
            />
          </LineChart>
        </ResponsiveContainer>
      </Box>
    </Paper>
  );
};
