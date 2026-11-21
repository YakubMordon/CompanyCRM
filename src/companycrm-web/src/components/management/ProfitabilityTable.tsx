import React, { useState, useEffect, useCallback } from 'react';
import Paper from '@mui/material/Paper';
import Box from '@mui/material/Box';
import Typography from '@mui/material/Typography';
import Grid from '@mui/material/Grid';
import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableContainer from '@mui/material/TableContainer';
import TableHead from '@mui/material/TableHead';
import TableRow from '@mui/material/TableRow';
import Chip from '@mui/material/Chip';
import CircularProgress from '@mui/material/CircularProgress';
import { ProjectProfitabilityDto } from '../../types';
import { managementApi } from '../../services/managementApi';
import { computeEmployeeMargin } from './analyticsHelpers';

interface ProfitabilityTableProps {
  projectId?: string;
}

export const ProfitabilityTable: React.FC<ProfitabilityTableProps> = ({ projectId }) => {
  const [profitability, setProfitability] = useState<ProjectProfitabilityDto | null>(null);
  const [isLoading, setIsLoading] = useState(true);

  const loadData = useCallback(async () => {
    setIsLoading(true);
    try {
      const data = await managementApi.getProfitability(projectId || undefined);
      setProfitability(data);
    } catch (err) {
      console.error('Failed to load profitability:', err);
    } finally {
      setIsLoading(false);
    }
  }, [projectId]);

  useEffect(() => {
    loadData();
  }, [loadData]);

  const formatCurrency = (val: number) => {
    return new Intl.NumberFormat('en-US', {
      style: 'currency',
      currency: 'USD',
      maximumFractionDigits: 2,
    }).format(val);
  };

  if (isLoading) {
    return (
      <Paper variant="outlined" sx={{ p: 4, borderRadius: 2, textAlign: 'center' }}>
        <CircularProgress size={28} />
      </Paper>
    );
  }

  if (!profitability) {
    return (
      <Paper variant="outlined" sx={{ p: 4, borderRadius: 2, textAlign: 'center' }}>
        <Typography variant="body2" color="text.secondary">
          No financial data available for this project.
        </Typography>
      </Paper>
    );
  }

  const isProfitable = profitability.grossProfit >= 0;

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', gap: 3 }}>
      {/* Financial KPI Cards */}
      <Grid container spacing={2}>
        <Grid item xs={12} sm={6} md={2.4}>
          <Paper variant="outlined" sx={{ p: 2, borderRadius: 2 }}>
            <Typography variant="caption" color="text.secondary">
              Total Billable Hours
            </Typography>
            <Typography variant="h6" sx={{ fontWeight: 700 }}>
              {profitability.totalBillableHours.toFixed(1)}h
            </Typography>
          </Paper>
        </Grid>

        <Grid item xs={12} sm={6} md={2.4}>
          <Paper variant="outlined" sx={{ p: 2, borderRadius: 2 }}>
            <Typography variant="caption" color="text.secondary">
              Client Billed Revenue
            </Typography>
            <Typography variant="h6" sx={{ fontWeight: 700, color: 'success.main' }}>
              {formatCurrency(profitability.totalRevenue ?? profitability.totalClientRevenue ?? 0)}
            </Typography>
          </Paper>
        </Grid>

        <Grid item xs={12} sm={6} md={2.4}>
          <Paper variant="outlined" sx={{ p: 2, borderRadius: 2 }}>
            <Typography variant="caption" color="text.secondary">
              Internal Labor Cost
            </Typography>
            <Typography variant="h6" sx={{ fontWeight: 700, color: 'info.main' }}>
              {formatCurrency(profitability.totalInternalCost ?? profitability.totalInternalLaborCost ?? 0)}
            </Typography>
          </Paper>
        </Grid>

        <Grid item xs={12} sm={6} md={2.4}>
          <Paper variant="outlined" sx={{ p: 2, borderRadius: 2 }}>
            <Typography variant="caption" color="text.secondary">
              Gross Profit
            </Typography>
            <Typography
              variant="h6"
              sx={{ fontWeight: 700, color: isProfitable ? 'success.main' : 'error.main' }}
            >
              {formatCurrency(profitability.grossProfit ?? 0)}
            </Typography>
          </Paper>
        </Grid>

        <Grid item xs={12} sm={6} md={2.4}>
          <Paper variant="outlined" sx={{ p: 2, borderRadius: 2 }}>
            <Typography variant="caption" color="text.secondary">
              Gross Margin %
            </Typography>
            <Typography
              variant="h6"
              sx={{ fontWeight: 700, color: isProfitable ? 'success.main' : 'error.main' }}
            >
              {(profitability.grossMarginPercent ?? 0).toFixed(1)}%
            </Typography>
          </Paper>
        </Grid>
      </Grid>

      {/* Employee Breakdown Table */}
      <Paper variant="outlined" sx={{ p: 3, borderRadius: 2 }}>
        <Typography variant="h6" sx={{ fontWeight: 700, mb: 2 }}>
          Labor Cost vs. Billable Revenue Breakdown
        </Typography>

        <TableContainer>
          <Table size="small">
            <TableHead sx={{ bgcolor: 'action.hover' }}>
              <TableRow>
                <TableCell sx={{ fontWeight: 700 }}>Employee</TableCell>
                <TableCell sx={{ fontWeight: 700 }}>Department</TableCell>
                <TableCell align="right" sx={{ fontWeight: 700 }}>
                  Cost ($/hr)
                </TableCell>
                <TableCell align="right" sx={{ fontWeight: 700 }}>
                  Billable ($/hr)
                </TableCell>
                <TableCell align="right" sx={{ fontWeight: 700 }}>
                  Logged (h)
                </TableCell>
                <TableCell align="right" sx={{ fontWeight: 700 }}>
                  Billable (h)
                </TableCell>
                <TableCell align="right" sx={{ fontWeight: 700 }}>
                  Internal Cost ($)
                </TableCell>
                <TableCell align="right" sx={{ fontWeight: 700 }}>
                  Billed Revenue ($)
                </TableCell>
                <TableCell align="right" sx={{ fontWeight: 700 }}>
                  Contribution ($)
                </TableCell>
                <TableCell align="right" sx={{ fontWeight: 700 }}>
                  Margin %
                </TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {profitability.employeeBreakdowns.map((emp) => {
                const { revenue: empRevenue, marginPercent: empMargin, isProfitable: isEmpProfitable } =
                  computeEmployeeMargin(emp);
                return (
                  <TableRow key={emp.employeeId} hover>
                    <TableCell sx={{ fontWeight: 600 }}>{emp.fullName || emp.employeeName || 'Unknown'}</TableCell>
                    <TableCell>{emp.department || '-'}</TableCell>
                    <TableCell align="right">{formatCurrency(emp.hourlyCostRate)}</TableCell>
                    <TableCell align="right">{formatCurrency(emp.hourlyBillableRate)}</TableCell>
                    <TableCell align="right">{emp.loggedHours.toFixed(1)}h</TableCell>
                    <TableCell align="right">{emp.billableHours.toFixed(1)}h</TableCell>
                    <TableCell align="right">{formatCurrency(emp.internalCost)}</TableCell>
                    <TableCell align="right" sx={{ color: 'success.main', fontWeight: 600 }}>
                      {formatCurrency(empRevenue)}
                    </TableCell>
                    <TableCell
                      align="right"
                      sx={{
                        color: isEmpProfitable ? 'success.main' : 'error.main',
                        fontWeight: 700,
                      }}
                    >
                      {formatCurrency(emp.grossProfit)}
                    </TableCell>
                    <TableCell align="right">
                      <Chip
                        label={`${empMargin.toFixed(1)}%`}
                        size="small"
                        color={isEmpProfitable ? 'success' : 'error'}
                        variant="outlined"
                        sx={{ fontWeight: 700, fontSize: '0.65rem' }}
                      />
                    </TableCell>
                  </TableRow>
                );
              })}
            </TableBody>
          </Table>
        </TableContainer>
      </Paper>
    </Box>
  );
};
