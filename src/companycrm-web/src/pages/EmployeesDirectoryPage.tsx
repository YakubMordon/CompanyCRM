import React, { useState, useEffect, useMemo, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import { DataGrid, GridColDef, GridRenderCellParams } from '@mui/x-data-grid';
import Box from '@mui/material/Box';
import Typography from '@mui/material/Typography';
import TextField from '@mui/material/TextField';
import InputAdornment from '@mui/material/InputAdornment';
import SearchIcon from '@mui/icons-material/Search';
import Avatar from '@mui/material/Avatar';
import Chip from '@mui/material/Chip';
import Button from '@mui/material/Button';
import IconButton from '@mui/material/IconButton';
import FormControl from '@mui/material/FormControl';
import InputLabel from '@mui/material/InputLabel';
import Select from '@mui/material/Select';
import MenuItem from '@mui/material/MenuItem';
import Tooltip from '@mui/material/Tooltip';
import Dialog from '@mui/material/Dialog';
import DialogTitle from '@mui/material/DialogTitle';
import DialogContent from '@mui/material/DialogContent';
import DialogContentText from '@mui/material/DialogContentText';
import DialogActions from '@mui/material/DialogActions';
import Alert from '@mui/material/Alert';
import CircularProgress from '@mui/material/CircularProgress';
import AccountCircleIcon from '@mui/icons-material/AccountCircle';
import PersonAddIcon from '@mui/icons-material/PersonAdd';
import EditIcon from '@mui/icons-material/Edit';
import DeleteOutlineIcon from '@mui/icons-material/DeleteOutline';
import { EmployeeDirectoryDto } from '../types';
import { employeesApi } from '../services/employeesApi';
import { managementApi } from '../services/managementApi';
import { useProject } from '../contexts/ProjectContext';
import { useAuth } from '../contexts/AuthContext';
import { TruncatedText } from '../components/common/TruncatedText';
import { EmployeeManagementDialog } from '../components/management/EmployeeManagementDialog';

export const EmployeesDirectoryPage: React.FC = () => {
  const navigate = useNavigate();
  const { role, currentUser } = useAuth();
  const isManagement = role === 'HigherManagement';
  const { projects, activeProject } = useProject();

  const [employees, setEmployees] = useState<EmployeeDirectoryDto[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [search, setSearch] = useState('');
  const [selectedDepartment, setSelectedDepartment] = useState('All');
  const [selectedProjectId, setSelectedProjectId] = useState<string>(activeProject?.id || 'ALL');

  // Management Dialog state
  const [dialogOpen, setDialogOpen] = useState(false);
  const [selectedEmployee, setSelectedEmployee] = useState<EmployeeDirectoryDto | null>(null);

  // Delete dialog state
  const [deleteTarget, setDeleteTarget] = useState<EmployeeDirectoryDto | null>(null);
  const [isDeleting, setIsDeleting] = useState(false);
  const [deleteError, setDeleteError] = useState<string | null>(null);

  useEffect(() => {
    setSelectedProjectId(activeProject?.id || 'ALL');
  }, [activeProject]);

  const loadEmployees = useCallback(() => {
    setIsLoading(true);
    const projId = selectedProjectId === 'ALL' ? undefined : selectedProjectId;
    employeesApi
      .getEmployees(undefined, undefined, projId)
      .then(setEmployees)
      .catch(console.error)
      .finally(() => setIsLoading(false));
  }, [selectedProjectId]);

  useEffect(() => {
    loadEmployees();
  }, [loadEmployees]);

  const handleOpenAdd = () => {
    setSelectedEmployee(null);
    setDialogOpen(true);
  };

  const handleOpenEdit = (emp: EmployeeDirectoryDto) => {
    setSelectedEmployee(emp);
    setDialogOpen(true);
  };

  const handleOpenDelete = (emp: EmployeeDirectoryDto) => {
    setDeleteError(null);
    setDeleteTarget(emp);
  };

  const handleConfirmDelete = async () => {
    if (!deleteTarget) return;
    setIsDeleting(true);
    setDeleteError(null);
    try {
      await managementApi.deleteEmployee(deleteTarget.id);
      setDeleteTarget(null);
      loadEmployees();
    } catch (err: any) {
      setDeleteError(err.response?.data?.message || err.message || 'Failed to delete employee');
    } finally {
      setIsDeleting(false);
    }
  };

  const departments = useMemo(() => {
    const set = new Set<string>();
    employees.forEach((e) => {
      if (e.department) set.add(e.department);
    });
    return ['All', ...Array.from(set)];
  }, [employees]);

  const filteredEmployees = useMemo(() => {
    return employees.filter((emp) => {
      const matchesDept = selectedDepartment === 'All' || emp.department === selectedDepartment;
      const q = search.toLowerCase().trim();
      const matchesSearch =
        !q ||
        emp.fullName.toLowerCase().includes(q) ||
        emp.email.toLowerCase().includes(q) ||
        emp.jobTitle.toLowerCase().includes(q) ||
        emp.department.toLowerCase().includes(q);
      return matchesDept && matchesSearch;
    });
  }, [employees, search, selectedDepartment]);

  const cellCenterSx = {
    display: 'flex',
    alignItems: 'center',
    height: '100%',
    width: '100%',
    minWidth: 0,
  };

  const columns: GridColDef[] = [
    {
      field: 'fullName',
      headerName: 'Employee',
      flex: 1.2,
      minWidth: 220,
      renderCell: (params: GridRenderCellParams<EmployeeDirectoryDto>) => (
        <Box sx={{ ...cellCenterSx, gap: 1.5, width: '100%' }}>
          <Avatar
            sx={{
              width: 34,
              height: 34,
              bgcolor: 'primary.main',
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
              overflow: 'hidden',
              minWidth: 0,
              flex: 1,
            }}
          >
            <TruncatedText
              text={params.row.fullName}
              maxWidth="100%"
              variant="body2"
              sx={{ fontWeight: 600, lineHeight: 1.2 }}
            />
            <TruncatedText
              text={params.row.email}
              maxWidth="100%"
              variant="caption"
              color="text.secondary"
              sx={{ lineHeight: 1.2 }}
            />
          </Box>
        </Box>
      ),
    },
    {
      field: 'department',
      headerName: 'Department & Role',
      flex: 1,
      minWidth: 160,
      renderCell: (params: GridRenderCellParams<EmployeeDirectoryDto>) => (
        <Box
          sx={{
            display: 'flex',
            flexDirection: 'column',
            justifyContent: 'center',
            overflow: 'hidden',
            width: '100%',
            minWidth: 0,
          }}
        >
          <TruncatedText
            text={params.row.jobTitle}
            maxWidth="100%"
            variant="body2"
            sx={{ fontWeight: 500, lineHeight: 1.2 }}
          />
          <TruncatedText
            text={params.row.department}
            maxWidth="100%"
            variant="caption"
            color="text.secondary"
            sx={{ lineHeight: 1.2 }}
          />
        </Box>
      ),
    },
    {
      field: 'seniorityRank',
      headerName: 'Seniority',
      width: 120,
      renderCell: (params: GridRenderCellParams<EmployeeDirectoryDto>) => (
        <Box sx={cellCenterSx}>
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
            sx={{ fontWeight: 600, fontSize: '0.7rem' }}
          />
        </Box>
      ),
    },
    {
      field: 'skills',
      headerName: 'Top Skills',
      flex: 1.5,
      minWidth: 240,
      sortable: false,
      renderCell: (params: GridRenderCellParams<EmployeeDirectoryDto>) => {
        const skills = params.row.skills || [];
        if (skills.length === 0) {
          return (
            <Box sx={cellCenterSx}>
              <Typography variant="caption" color="text.secondary">
                No verified skills
              </Typography>
            </Box>
          );
        }

        const extraSkills = skills
          .slice(3)
          .map((s) => `${s.skillName} (${s.proficiencyLevel}★)`)
          .join(', ');

        return (
          <Box
            sx={{
              ...cellCenterSx,
              gap: 0.5,
              width: '100%',
              minWidth: 0,
              overflow: 'hidden',
            }}
          >
            {skills.slice(0, 3).map((s) => (
              <Tooltip key={s.id} title={`${s.skillName} - Level ${s.proficiencyLevel}/5`}>
                <Chip
                  label={`${s.skillName} (${s.proficiencyLevel}★)`}
                  size="small"
                  variant="outlined"
                  sx={{
                    height: 22,
                    fontSize: '0.65rem',
                    minWidth: 0,
                    maxWidth: 115,
                    '& .MuiChip-label': {
                      overflow: 'hidden',
                      textOverflow: 'ellipsis',
                      whiteSpace: 'nowrap',
                    },
                  }}
                />
              </Tooltip>
            ))}

            {skills.length > 3 && (
              <Tooltip title={`Additional skills: ${extraSkills}`}>
                <Chip
                  label={`+${skills.length - 3}`}
                  size="small"
                  color="secondary"
                  variant="outlined"
                  sx={{
                    height: 22,
                    fontSize: '0.65rem',
                    cursor: 'pointer',
                    flexShrink: 0,
                  }}
                />
              </Tooltip>
            )}
          </Box>
        );
      },
    },
    {
      field: 'actions',
      headerName: 'Actions',
      width: isManagement ? 210 : 140,
      align: 'center',
      headerAlign: 'center',
      sortable: false,
      renderCell: (params: GridRenderCellParams<EmployeeDirectoryDto>) => {
        const isSelf = currentUser?.id === params.row.id;
        return (
          <Box sx={{ ...cellCenterSx, justifyContent: 'center', gap: 0.5 }}>
            <Tooltip title={`View full profile and skills for ${params.row.fullName}`}>
              <Button
                size="small"
                startIcon={<AccountCircleIcon />}
                onClick={() => navigate(`/profile?id=${params.row.id}`)}
                sx={{ textTransform: 'none', fontWeight: 600 }}
              >
                Profile
              </Button>
            </Tooltip>
            {isManagement && (
              <>
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
              </>
            )}
          </Box>
        );
      },
    },
  ];

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2.5 }}>
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
            Employee Directory
          </Typography>
          <Typography variant="body2" color="text.secondary">
            Enterprise team members, roles, departments, and verified skills matrices.
          </Typography>
        </Box>

        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5, flexWrap: 'wrap' }}>
          {isManagement && (
            <Button
              variant="contained"
              color="primary"
              startIcon={<PersonAddIcon />}
              onClick={handleOpenAdd}
              sx={{ mr: 1 }}
            >
              Add Employee
            </Button>
          )}

          <Tooltip title="Filter employees by name, email, job title or department">
            <TextField
              size="small"
              placeholder="Search name, email, title..."
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              InputProps={{
                startAdornment: (
                  <InputAdornment position="start">
                    <SearchIcon fontSize="small" />
                  </InputAdornment>
                ),
              }}
              sx={{ width: { xs: 200, sm: 260 } }}
            />
          </Tooltip>

          <FormControl size="small" sx={{ width: 180 }}>
            <InputLabel>Project Scope</InputLabel>
            <Tooltip title="Filter employees overall or by specific project">
              <Select
                value={selectedProjectId}
                label="Project Scope"
                onChange={(e) => setSelectedProjectId(e.target.value)}
              >
                <MenuItem value="ALL">
                  <em>All Projects (Overall)</em>
                </MenuItem>
                {projects.map((p) => (
                  <MenuItem key={p.id} value={p.id}>
                    {p.name} ({p.code})
                  </MenuItem>
                ))}
              </Select>
            </Tooltip>
          </FormControl>

          <FormControl size="small" sx={{ width: 160 }}>
            <InputLabel>Department</InputLabel>
            <Tooltip title="Filter directory by specific department">
              <Select
                value={selectedDepartment}
                label="Department"
                onChange={(e) => setSelectedDepartment(e.target.value)}
              >
                {departments.map((dept) => (
                  <MenuItem key={dept} value={dept}>
                    {dept}
                  </MenuItem>
                ))}
              </Select>
            </Tooltip>
          </FormControl>
        </Box>
      </Box>

      <Box sx={{ height: 'calc(100vh - 210px)', width: '100%' }}>
        <DataGrid
          rows={filteredEmployees}
          columns={columns}
          loading={isLoading}
          rowHeight={60}
          pageSizeOptions={[10, 25, 50]}
          initialState={{
            pagination: { paginationModel: { pageSize: 25 } },
          }}
          disableRowSelectionOnClick
          sx={{
            borderRadius: 2,
            borderColor: 'divider',
            '& .MuiDataGrid-cell': {
              display: 'flex',
              alignItems: 'center',
            },
            '& .MuiDataGrid-columnHeaders': {
              bgcolor: 'action.hover',
              fontWeight: 700,
            },
          }}
        />
      </Box>

      {/* Add / Edit Dialog */}
      <EmployeeManagementDialog
        open={dialogOpen}
        onClose={() => setDialogOpen(false)}
        onSuccess={loadEmployees}
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

export default EmployeesDirectoryPage;
