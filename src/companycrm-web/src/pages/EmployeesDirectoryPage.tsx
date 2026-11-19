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
import AccountCircleIcon from '@mui/icons-material/AccountCircle';
import { EmployeeDirectoryDto } from '../types';
import { employeesApi } from '../services/employeesApi';

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

  const columns: GridColDef[] = [
    {
      field: 'fullName',
      headerName: 'Employee',
      flex: 1,
      minWidth: 200,
      renderCell: (params: GridRenderCellParams<EmployeeDirectoryDto>) => (
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5 }}>
          <Avatar sx={{ width: 32, height: 32, bgcolor: 'primary.main', fontWeight: 600 }}>
            {params.row.fullName.charAt(0)}
          </Avatar>
          <Box>
            <Typography variant="body2" sx={{ fontWeight: 600 }}>
              {params.row.fullName}
            </Typography>
            <Typography variant="caption" color="text.secondary">
              {params.row.email}
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
      field: 'jobTitle',
      headerName: 'Job Title',
      width: 180,
    },
    {
      field: 'seniorityRank',
      headerName: 'Seniority',
      width: 110,
      renderCell: (params: GridRenderCellParams<EmployeeDirectoryDto>) => (
        <Chip
          label={params.value}
          size="small"
          variant="outlined"
          sx={{ fontWeight: 600, fontSize: '0.7rem' }}
        />
      ),
    },
    {
      field: 'role',
      headerName: 'System Role',
      width: 130,
      renderCell: (params: GridRenderCellParams<EmployeeDirectoryDto>) => (
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
      ),
    },
    {
      field: 'skills',
      headerName: 'Top Skills Matrix',
      flex: 1.2,
      minWidth: 240,
      renderCell: (params: GridRenderCellParams<EmployeeDirectoryDto>) => {
        const skills = params.row.skills || [];
        return (
          <Box sx={{ display: 'flex', gap: 0.5, flexWrap: 'nowrap', overflow: 'hidden' }}>
            {skills.slice(0, 3).map((s) => (
              <Chip
                key={s.id}
                label={`${s.skillName} (${s.proficiencyLevel}★)`}
                size="small"
                variant="outlined"
                sx={{ height: 22, fontSize: '0.65rem' }}
              />
            ))}
            {skills.length > 3 && (
              <Chip
                label={`+${skills.length - 3}`}
                size="small"
                sx={{ height: 22, fontSize: '0.65rem' }}
              />
            )}
          </Box>
        );
      },
    },
    {
      field: 'actions',
      headerName: 'Actions',
      width: 130,
      sortable: false,
      renderCell: (params: GridRenderCellParams<EmployeeDirectoryDto>) => (
        <Button
          size="small"
          startIcon={<AccountCircleIcon />}
          onClick={() => navigate(`/profile?id=${params.row.id}`)}
          sx={{ textTransform: 'none', fontWeight: 600 }}
        >
          View Profile
        </Button>
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

          <FormControl size="small" sx={{ width: 160 }}>
            <InputLabel>Department</InputLabel>
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
          </FormControl>
        </Box>
      </Box>

      <Box sx={{ height: 'calc(100vh - 210px)', width: '100%' }}>
        <DataGrid
          rows={filteredEmployees}
          columns={columns}
          loading={isLoading}
          pageSizeOptions={[10, 25, 50]}
          initialState={{
            pagination: { paginationModel: { pageSize: 25 } },
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
    </Box>
  );
};
