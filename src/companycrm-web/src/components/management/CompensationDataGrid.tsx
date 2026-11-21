import React, { useState, useEffect, useMemo } from 'react';
import { DataGrid, GridColDef, GridRenderCellParams } from '@mui/x-data-grid';
import Paper from '@mui/material/Paper';
import Box from '@mui/material/Box';
import Typography from '@mui/material/Typography';
import Grid from '@mui/material/Grid';
import LinearProgress from '@mui/material/LinearProgress';
import Chip from '@mui/material/Chip';
import Avatar from '@mui/material/Avatar';
import { alpha } from '@mui/material/styles';
import AttachMoneyIcon from '@mui/icons-material/AttachMoney';
import TrendingUpIcon from '@mui/icons-material/TrendingUp';
import AssignmentTurnedInIcon from '@mui/icons-material/AssignmentTurnedIn';
import { EmployeeCompensationDto } from '../../types';
import { managementApi } from '../../services/managementApi';

export const CompensationDataGrid: React.FC = () => {
  const [data, setData] = useState<EmployeeCompensationDto[]>([]);
  const [isLoading, setIsLoading] = useState(true);

  useEffect(() => {
    setIsLoading(true);
    managementApi
      .getCompensation()
      .then(setData)
      .catch(console.error)
      .finally(() => setIsLoading(false));
  }, []);

  const formatCurrency = (val: number) => {
    return new Intl.NumberFormat('en-US', {
      style: 'currency',
      currency: 'USD',
      maximumFractionDigits: 2,
    }).format(val);
  };

  const summary = useMemo(() => {
    if (data.length === 0) {
      return { totalPayroll: 0, avgUtilization: 0, avgCompliance: 0 };
    }
    const totalPayroll = data.reduce((sum, e) => sum + e.annualBaseSalary, 0);
    const avgUtilization =
      data.reduce((sum, e) => sum + e.actualUtilizationPercent, 0) / data.length;
    const avgCompliance =
      data.reduce((sum, e) => sum + e.dailyPlanCompliancePercent, 0) / data.length;
    return { totalPayroll, avgUtilization, avgCompliance };
  }, [data]);

  const columns: GridColDef[] = [
    {
      field: 'fullName',
      headerName: 'Employee',
      flex: 1,
      minWidth: 190,
      renderCell: (params: GridRenderCellParams<EmployeeCompensationDto>) => (
        <Box
          sx={{
            display: 'flex',
            alignItems: 'center',
            gap: 1.5,
            width: '100%',
            height: '100%',
          }}
        >
          <Avatar
            sx={{
              width: 30,
              height: 30,
              bgcolor: 'primary.dark',
              fontWeight: 600,
              flexShrink: 0,
            }}
          >
            {params.row.fullName.charAt(0)}
          </Avatar>

          <Box
            sx={{
              display: 'flex',
              flexDirection: 'column',
              justifyContent: 'center',
              gap: '1px',
              minWidth: 0,
              flexGrow: 1,
              overflow: 'hidden',
            }}
          >
            <Typography
              variant="body2"
              noWrap
              sx={{ fontWeight: 600, lineHeight: 1.1 }}
            >
              {params.row.fullName}
            </Typography>
            <Typography
              variant="caption"
              color="text.secondary"
              noWrap
              sx={{ lineHeight: 1.1 }}
            >
              {params.row.department}
            </Typography>
          </Box>
        </Box>
      ),
    },
    {
      field: 'jobTitle',
      headerName: 'Title',
      width: 170,
    },
    {
      field: 'role',
      headerName: 'Role',
      width: 120,
      renderCell: (params: GridRenderCellParams<EmployeeCompensationDto>) => (
        <Chip
          label={params.value}
          size="small"
          color={
            params.value === 'HigherManagement'
              ? 'success'
              : params.value === 'TeamLead'
              ? 'warning'
              : 'primary'
          }
          sx={{ fontWeight: 600, fontSize: '0.68rem' }}
        />
      ),
    },
    {
      field: 'annualBaseSalary',
      headerName: 'Base Salary',
      width: 140,
      type: 'number',
      valueFormatter: (val: number) => formatCurrency(val),
    },
    {
      field: 'hourlyCostRate',
      headerName: 'Cost Rate',
      width: 110,
      type: 'number',
      valueFormatter: (val: number) => `${formatCurrency(val)}/hr`,
    },
    {
      field: 'hourlyBillableRate',
      headerName: 'Billable Rate',
      width: 120,
      type: 'number',
      valueFormatter: (val: number) => `${formatCurrency(val)}/hr`,
    },
    {
      field: 'actualUtilizationPercent',
      headerName: 'Utilization %',
      width: 170,
      renderCell: (params: GridRenderCellParams<EmployeeCompensationDto>) => {
        const val = params.value as number;
        const target = params.row.targetUtilizationPercent;
        const isGood = val >= target;
        return (
          <Box
            sx={{
              display: 'flex',
              flexDirection: 'column',
              justifyContent: 'center',
              height: '100%',
              width: '100%',
              pr: 1,
            }}
          >
            <Box sx={{ display: 'flex', justifyContent: 'space-between', mb: 0.5 }}>
              <Typography variant="caption" sx={{ fontWeight: 700, lineHeight: 1.1 }}>
                {val.toFixed(1)}%
              </Typography>
              <Typography variant="caption" color="text.secondary" sx={{ lineHeight: 1.1 }}>
                Target: {target.toFixed(0)}%
              </Typography>
            </Box>
            <LinearProgress
              variant="determinate"
              value={Math.min(100, val)}
              color={isGood ? 'success' : 'warning'}
              sx={{ height: 6, borderRadius: 3 }}
            />
          </Box>
        );
      },
    },
    {
      field: 'dailyPlanCompliancePercent',
      headerName: 'Plan Compliance %',
      width: 170,
      renderCell: (params: GridRenderCellParams<EmployeeCompensationDto>) => {
        const val = params.value as number;
        const isHigh = val >= 80;
        return (
          <Box
            sx={{
              display: 'flex',
              flexDirection: 'column',
              justifyContent: 'center',
              height: '100%',
              width: '100%',
              pr: 1,
            }}
          >
            <Box sx={{ display: 'flex', justifyContent: 'space-between', mb: 0.5 }}>
              <Typography variant="caption" sx={{ fontWeight: 700, lineHeight: 1.1 }}>
                {val.toFixed(1)}%
              </Typography>
              <Typography variant="caption" color="text.secondary" sx={{ lineHeight: 1.1 }}>
                On-Time: {params.row.onTimeSubmissionPercent.toFixed(0)}%
              </Typography>
            </Box>
            <LinearProgress
              variant="determinate"
              value={Math.min(100, val)}
              color={isHigh ? 'success' : 'error'}
              sx={{ height: 6, borderRadius: 3 }}
            />
          </Box>
        );
      },
    },
  ];

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', gap: 3 }}>
      {/* Summary KPI Cards */}
      <Grid container spacing={2}>
        <Grid item xs={12} sm={4}>
          <Paper variant="outlined" sx={{ p: 2.5, borderRadius: 2 }}>
            <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5 }}>
              <Avatar sx={{ bgcolor: (theme) => alpha(theme.palette.primary.main, 0.12), color: 'primary.main' }}>
                <AttachMoneyIcon />
              </Avatar>
              <Box>
                <Typography variant="caption" color="text.secondary">
                  Total Annual Payroll Run Rate
                </Typography>
                <Typography variant="h6" sx={{ fontWeight: 700 }}>
                  {formatCurrency(summary.totalPayroll)}
                </Typography>
              </Box>
            </Box>
          </Paper>
        </Grid>

        <Grid item xs={12} sm={4}>
          <Paper variant="outlined" sx={{ p: 2.5, borderRadius: 2 }}>
            <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5 }}>
              <Avatar sx={{ bgcolor: (theme) => alpha(theme.palette.success.main, 0.12), color: 'success.main' }}>
                <TrendingUpIcon />
              </Avatar>
              <Box>
                <Typography variant="caption" color="text.secondary">
                  Average Team Utilization
                </Typography>
                <Typography variant="h6" sx={{ fontWeight: 700, color: 'success.main' }}>
                  {summary.avgUtilization.toFixed(1)}%
                </Typography>
              </Box>
            </Box>
          </Paper>
        </Grid>

        <Grid item xs={12} sm={4}>
          <Paper variant="outlined" sx={{ p: 2.5, borderRadius: 2 }}>
            <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5 }}>
              <Avatar sx={{ bgcolor: (theme) => alpha(theme.palette.info.main, 0.12), color: 'info.main' }}>
                <AssignmentTurnedInIcon />
              </Avatar>
              <Box>
                <Typography variant="caption" color="text.secondary">
                  Daily Plan Submission Compliance
                </Typography>
                <Typography variant="h6" sx={{ fontWeight: 700, color: 'info.main' }}>
                  {summary.avgCompliance.toFixed(1)}%
                </Typography>
              </Box>
            </Box>
          </Paper>
        </Grid>
      </Grid>

      {/* Compensation DataGrid */}
      <Paper variant="outlined" sx={{ p: 3, borderRadius: 2 }}>
        <Typography variant="h6" sx={{ fontWeight: 700, mb: 2 }}>
          Executive Compensation & Performance Metrics
        </Typography>

        <Box sx={{ height: 420, width: '100%' }}>
          <DataGrid
            rows={data}
            getRowId={(r) => r.employeeId}
            columns={columns}
            loading={isLoading}
            pageSizeOptions={[10, 25, 50]}
            initialState={{
              pagination: { paginationModel: { pageSize: 10 } },
            }}
            disableRowSelectionOnClick
            sx={{
              borderRadius: 2,
              borderColor: 'divider',
              '& .MuiDataGrid-columnHeaders': {
                bgcolor: 'action.hover',
                fontWeight: 700,
              },
            }}
          />
        </Box>
      </Paper>
    </Box>
  );
};
