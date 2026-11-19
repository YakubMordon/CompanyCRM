import React, { useState, useEffect } from 'react';
import Paper from '@mui/material/Paper';
import Box from '@mui/material/Box';
import Typography from '@mui/material/Typography';
import CircularProgress from '@mui/material/CircularProgress';
import { PieChart, Pie, Cell, Tooltip, ResponsiveContainer, Legend } from 'recharts';
import { CompletionRateDto } from '../../types';
import { managementApi } from '../../services/managementApi';
import { buildCompletionChartData } from './analyticsHelpers';

interface CompletionRatesChartProps {
  projectId: string;
}

const STATUS_COLORS = ['#64748b', '#0284c7', '#d97706', '#7c3aed', '#16a34a'];

export const CompletionRatesChart: React.FC<CompletionRatesChartProps> = ({ projectId }) => {
  const [data, setData] = useState<CompletionRateDto | null>(null);
  const [isLoading, setIsLoading] = useState(true);

  useEffect(() => {
    setIsLoading(true);
    managementApi
      .getCompletionRates(projectId)
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

  const chartData = buildCompletionChartData(data);

  const totalItemsCount = data.totalWorkItems ?? data.totalItems ?? 0;

  return (
    <Paper variant="outlined" sx={{ p: 2.5, borderRadius: 2 }}>
      <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', mb: 1 }}>
        <Typography variant="subtitle1" sx={{ fontWeight: 700 }}>
          Work Item Completion Rate
        </Typography>
        <Typography variant="subtitle2" sx={{ fontWeight: 700, color: 'success.main' }}>
          {(data.overallCompletionPercent ?? 0).toFixed(1)}% Completed
        </Typography>
      </Box>
      <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mb: 2 }}>
        Status distribution of {totalItemsCount} work items in active project.
      </Typography>

      <Box sx={{ width: '100%', height: 260 }}>
        <ResponsiveContainer width="100%" height="100%">
          <PieChart>
            <Pie
              data={chartData}
              cx="50%"
              cy="50%"
              innerRadius={60}
              outerRadius={90}
              paddingAngle={4}
              dataKey="value"
            >
              {chartData.map((_entry, index) => (
                <Cell key={`cell-${index}`} fill={STATUS_COLORS[index % STATUS_COLORS.length]} />
              ))}
            </Pie>
            <Tooltip />
            <Legend />
          </PieChart>
        </ResponsiveContainer>
      </Box>
    </Paper>
  );
};
