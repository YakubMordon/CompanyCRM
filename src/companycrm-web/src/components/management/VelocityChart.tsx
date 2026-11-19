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
import { SprintVelocityDto } from '../../types';
import { managementApi } from '../../services/managementApi';

interface VelocityChartProps {
  projectId: string;
}

export const VelocityChart: React.FC<VelocityChartProps> = ({ projectId }) => {
  const [data, setData] = useState<SprintVelocityDto[]>([]);
  const [isLoading, setIsLoading] = useState(true);

  useEffect(() => {
    setIsLoading(true);
    managementApi
      .getVelocity(projectId, 5)
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
        Team Velocity (Story Points per Sprint)
      </Typography>
      <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mb: 2 }}>
        Planned Story Points vs. Completed Story Points over recent sprints.
      </Typography>

      <Box sx={{ width: '100%', height: 260 }}>
        <ResponsiveContainer width="100%" height="100%">
          <BarChart data={data} margin={{ top: 10, right: 20, left: 0, bottom: 5 }}>
            <CartesianGrid strokeDasharray="3 3" vertical={false} />
            <XAxis dataKey="sprintName" />
            <YAxis />
            <Tooltip />
            <Legend />
            <Bar dataKey="plannedPoints" name="Planned SP" fill="#1976d2" radius={[4, 4, 0, 0]} />
            <Bar dataKey="completedPoints" name="Completed SP" fill="#2e7d32" radius={[4, 4, 0, 0]} />
          </BarChart>
        </ResponsiveContainer>
      </Box>
    </Paper>
  );
};
