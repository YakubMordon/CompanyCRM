import React, { useState, useEffect, useMemo, useCallback } from 'react';
import { DataGrid, GridColDef, GridRenderCellParams } from '@mui/x-data-grid';
import Paper from '@mui/material/Paper';
import Box from '@mui/material/Box';
import Typography from '@mui/material/Typography';
import Grid from '@mui/material/Grid';
import LinearProgress from '@mui/material/LinearProgress';
import Chip from '@mui/material/Chip';
import Avatar from '@mui/material/Avatar';
import Button from '@mui/material/Button';
import IconButton from '@mui/material/IconButton';
import Tooltip from '@mui/material/Tooltip';
import Dialog from '@mui/material/Dialog';
import DialogTitle from '@mui/material/DialogTitle';
import DialogContent from '@mui/material/DialogContent';
import DialogContentText from '@mui/material/DialogContentText';
import DialogActions from '@mui/material/DialogActions';
import Alert from '@mui/material/Alert';
import CircularProgress from '@mui/material/CircularProgress';
import { alpha } from '@mui/material/styles';
import AttachMoneyIcon from '@mui/icons-material/AttachMoney';
import TrendingUpIcon from '@mui/icons-material/TrendingUp';
import AssignmentTurnedInIcon from '@mui/icons-material/AssignmentTurnedIn';
import PersonAddIcon from '@mui/icons-material/PersonAdd';
import EditIcon from '@mui/icons-material/Edit';
import DeleteOutlineIcon from '@mui/icons-material/DeleteOutline';
import { EmployeeCompensationDto } from '../../types';
import { managementApi } from '../../services/managementApi';
import { useAuth } from '../../contexts/AuthContext';
import { EmployeeManagementDialog } from './EmployeeManagementDialog';

export const CompensationDataGrid: React.FC = () => {
  const { currentUser } = useAuth();
  const [data, setData] = useState<EmployeeCompensationDto[]>([]);
  const [isLoading, setIsLoading] = useState(true);

  // Management Dialog state
  const [dialogOpen, setDialogOpen] = useState(false);
  const [selectedEmployee, setSelectedEmployee] = useState<EmployeeCompensationDto | null>(null);

  // Delete confirmation dialog state
  const [deleteTarget, setDeleteTarget] = useState<EmployeeCompensationDto | null>(null);
  const [isDeleting, setIsDeleting] = useState(false);
  const [deleteError, setDeleteError] = useState<string | null>(null);

  const loadData = useCallback(() => {
    setIsLoading(true);
    managementApi
      .getCompensation()
      .then(setData)
      .catch(console.error)
      .finally(() => setIsLoading(false));
  }, []);

  useEffect(() => {
    loadData();
  }, [loadData]);

  const handleOpenAdd = () => {
    setSelectedEmployee(null);
    setDialogOpen(true);
  };

  const handleOpenEdit = (emp: EmployeeCompensationDto) => {
    setSelectedEmployee(emp);
    setDialogOpen(true);
  };

  const handleOpenDelete = (emp: EmployeeCompensationDto) => {
    setDeleteError(null);
    setDeleteTarget(emp);
  };

  const handleConfirmDelete = async () => {
    if (!deleteTarget) return;
    setIsDeleting(true);
    setDeleteError(null);
    try {
      await managementApi.deleteEmployee(deleteTarget.employeeId);
      setDeleteTarget(null);
      loadData();
    } catch (err: any) {
      setDeleteError(err.response?.data?.message || err.message || 'Failed to delete employee');
    } finally {
      setIsDeleting(false);
    }
  };

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
            overflow: 'hidden',
          }}
        >
          <Avatar
            src={params.row.department} // Avatar image placeholder
            alt={params.value}
            sx={{ width: 32, height: 32, fontSize: '0.85rem' }}
          >
            {params.value?.[0]}
          </Avatar>
          <Box sx={{ overflow: 'hidden' }}>
            <Typography variant="body2" sx={{ fontWeight: 600 }} noWrap>
              {params.value}
            </Typography>
            <Typography variant="caption" color="text.secondary" noWrap display="block">
              {params.row.jobTitle}
            </Typography>
          </Box>
        </Box>
      ),
    },
    {
      field: 'department',
      headerName: 'Department',
      width: 140,
    },
    {
      field: 'seniorityRank',
      headerName: 'Seniority',
      width: 110,
      renderCell: (params: GridRenderCellParams<EmployeeCompensationDto>) => (
        <Chip
          label={params.value}
          size="small"
          variant="outlined"
          color={
            params.value === 'Principal'
              ? 'secondary'
              : params.value === 'Lead'
              ? 'primary'
              : params.value === 'Senior'
              ? 'info'
              : 'default'
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
    {
      field: 'actions',
      headerName: 'Actions',
      width: 110,
      sortable: false,
      filterable: false,
      renderCell: (params: GridRenderCellParams<EmployeeCompensationDto>) => {
        const isSelf = currentUser?.id === params.row.employeeId;
        return (
          <Box sx={{ display: 'flex', gap: 0.5 }}>
            <Tooltip title="Edit Employee">
              <IconButton
                size="small"
                color="primary"
                onClick={() => handleOpenEdit(params.row)}
              >
                <EditIcon fontSize="small" />
              </IconButton>
            </Tooltip>
            <Tooltip
              title={
                isSelf ? 'Cannot delete your own active account' : 'Delete Employee'
              }
            >
              <span>
                <IconButton
                  size="small"
                  color="error"
                  disabled={isSelf}
                  onClick={() => handleOpenDelete(params.row)}
                >
                  <DeleteOutlineIcon fontSize="small" />
                </IconButton>
              </span>
            </Tooltip>
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
        <Box
          sx={{
            display: 'flex',
            justifyContent: 'space-between',
            alignItems: 'center',
            mb: 2,
          }}
        >
          <Typography variant="h6" sx={{ fontWeight: 700 }}>
            Executive Compensation & Performance Metrics
          </Typography>
          <Button
            variant="contained"
            color="primary"
            startIcon={<PersonAddIcon />}
            onClick={handleOpenAdd}
          >
            Add Employee
          </Button>
        </Box>

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

      {/* Employee Add/Edit Dialog */}
      <EmployeeManagementDialog
        open={dialogOpen}
        onClose={() => setDialogOpen(false)}
        onSuccess={loadData}
        employeeToEdit={selectedEmployee}
      />

      {/* Delete Confirmation Dialog */}
      <Dialog
        open={Boolean(deleteTarget)}
        onClose={isDeleting ? undefined : () => setDeleteTarget(null)}
      >
        <DialogTitle>Confirm Delete Employee</DialogTitle>
        <DialogContent>
          {deleteError && (
            <Alert severity="error" sx={{ mb: 2 }}>
              {deleteError}
            </Alert>
          )}
          <DialogContentText>
            Are you sure you want to delete <strong>{deleteTarget?.fullName}</strong> (
            {deleteTarget?.email})? This action cannot be undone. Any work items created by this
            employee will be reassigned to you.
          </DialogContentText>
        </DialogContent>
        <DialogActions sx={{ px: 3, pb: 2 }}>
          <Button onClick={() => setDeleteTarget(null)} disabled={isDeleting}>
            Cancel
          </Button>
          <Button
            variant="contained"
            color="error"
            onClick={handleConfirmDelete}
            disabled={isDeleting}
            startIcon={isDeleting ? <CircularProgress size={18} /> : undefined}
          >
            Delete
          </Button>
        </DialogActions>
      </Dialog>
    </Box>
  );
};
