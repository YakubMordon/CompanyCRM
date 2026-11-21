import React, { useState, useEffect, useMemo } from 'react';
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
import FormControl from '@mui/material/FormControl';
import InputLabel from '@mui/material/InputLabel';
import Select from '@mui/material/Select';
import MenuItem from '@mui/material/MenuItem';
import Tooltip from '@mui/material/Tooltip';
import AccountCircleIcon from '@mui/icons-material/AccountCircle';
import { EmployeeDirectoryDto } from '../types';
import { employeesApi } from '../services/employeesApi';
import { TruncatedText } from '../components/common/TruncatedText';

export const EmployeesDirectoryPage: React.FC = () => {
  const navigate = useNavigate();
  const [employees, setEmployees] = useState<EmployeeDirectoryDto[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [search, setSearch] = useState('');
  const [selectedDepartment, setSelectedDepartment] = useState('All');

  useEffect(() => {
    setIsLoading(true);
    employeesApi
      .getEmployees()
      .then(setEmployees)
      .catch(console.error)
      .finally(() => setIsLoading(false));
  }, []);

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
              gap: '2px',
              minWidth: 0,
              flexGrow: 1,
              overflow: 'hidden',
            }}
          >
            <TruncatedText
              text={params.row.fullName}
              variant="body2"
              sx={{ fontWeight: 600, lineHeight: 1.15 }}
            />
            <TruncatedText
              text={params.row.email}
              variant="caption"
              color="text.secondary"
              sx={{ lineHeight: 1.15 }}
            />
          </Box>
        </Box>
      ),
    },
    {
      field: 'department',
      headerName: 'Department',
      width: 140,
      renderCell: (params) => (
        <Box sx={cellCenterSx}>
          <TruncatedText text={params.value || '—'} variant="body2" />
        </Box>
      ),
    },
    {
      field: 'jobTitle',
      headerName: 'Job Title',
      width: 180,
      renderCell: (params) => (
        <Box sx={cellCenterSx}>
          <TruncatedText text={params.value || '—'} variant="body2" />
        </Box>
      ),
    },
    {
      field: 'seniorityRank',
      headerName: 'Seniority',
      width: 120,
      align: 'center',
      headerAlign: 'center',
      renderCell: (params: GridRenderCellParams<EmployeeDirectoryDto>) => (
        <Box sx={{ ...cellCenterSx, justifyContent: 'center' }}>
          <Tooltip title={`Seniority Level: ${params.value}`}>
            <Chip
              label={params.value}
              size="small"
              variant="outlined"
              sx={{ fontWeight: 600, fontSize: '0.7rem' }}
            />
          </Tooltip>
        </Box>
      ),
    },
    {
      field: 'role',
      headerName: 'System Role',
      width: 140,
      align: 'center',
      headerAlign: 'center',
      renderCell: (params: GridRenderCellParams<EmployeeDirectoryDto>) => (
        <Box sx={{ ...cellCenterSx, justifyContent: 'center' }}>
          <Tooltip title={`System Permission Role: ${params.value}`}>
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
              sx={{ fontWeight: 600, fontSize: '0.7rem' }}
            />
          </Tooltip>
        </Box>
      ),
    },
    {
      field: 'skills',
      headerName: 'Top Skills Matrix',
      flex: 1.3,
      minWidth: 260,
      renderCell: (params: GridRenderCellParams<EmployeeDirectoryDto>) => {
        const skills = params.row.skills || [];
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
      width: 140,
      align: 'center',
      headerAlign: 'center',
      sortable: false,
      renderCell: (params: GridRenderCellParams<EmployeeDirectoryDto>) => (
        <Box sx={{ ...cellCenterSx, justifyContent: 'center' }}>
          <Tooltip title={`View full profile and skills for ${params.row.fullName}`}>
            <Button
              size="small"
              startIcon={<AccountCircleIcon />}
              onClick={() => navigate(`/profile?id=${params.row.id}`)}
              sx={{ textTransform: 'none', fontWeight: 600 }}
            >
              View Profile
            </Button>
          </Tooltip>
        </Box>
      ),
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
    </Box>
  );
};

export default EmployeesDirectoryPage;
