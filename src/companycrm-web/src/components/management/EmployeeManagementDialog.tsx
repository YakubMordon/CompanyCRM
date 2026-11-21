import React, { useState, useEffect } from 'react';
import Dialog from '@mui/material/Dialog';
import DialogTitle from '@mui/material/DialogTitle';
import DialogContent from '@mui/material/DialogContent';
import DialogActions from '@mui/material/DialogActions';
import Button from '@mui/material/Button';
import TextField from '@mui/material/TextField';
import Grid from '@mui/material/Grid';
import FormControl from '@mui/material/FormControl';
import InputLabel from '@mui/material/InputLabel';
import Select from '@mui/material/Select';
import MenuItem from '@mui/material/MenuItem';
import FormHelperText from '@mui/material/FormHelperText';
import Alert from '@mui/material/Alert';
import CircularProgress from '@mui/material/CircularProgress';
import InputAdornment from '@mui/material/InputAdornment';
import IconButton from '@mui/material/IconButton';
import Tooltip from '@mui/material/Tooltip';
import CalculateIcon from '@mui/icons-material/Calculate';
import { CreateEmployeeRequestDto, UpdateEmployeeRequestDto, SeniorityRank, AppRole, EmployeeCompensationDto, EmployeeDirectoryDto } from '../../types';
import { employeeManagementSchema } from '../../validation/schemas';
import { managementApi } from '../../services/managementApi';

interface EmployeeManagementDialogProps {
  open: boolean;
  onClose: () => void;
  onSuccess: () => void;
  employeeToEdit?: EmployeeCompensationDto | EmployeeDirectoryDto | null;
}

export const EmployeeManagementDialog: React.FC<EmployeeManagementDialogProps> = ({
  open,
  onClose,
  onSuccess,
  employeeToEdit,
}) => {
  const isEditing = Boolean(employeeToEdit);

  const [fullName, setFullName] = useState('');
  const [email, setEmail] = useState('');
  const [department, setDepartment] = useState('');
  const [jobTitle, setJobTitle] = useState('');
  const [seniorityRank, setSeniorityRank] = useState<SeniorityRank>('Mid');
  const [role, setRole] = useState<AppRole>('Employee');
  const [annualBaseSalary, setAnnualBaseSalary] = useState<number | string>(75000);
  const [hourlyCostRate, setHourlyCostRate] = useState<number | string>(36.06);
  const [hourlyBillableRate, setHourlyBillableRate] = useState<number | string>(120);
  const [weeklyCapacityHours, setWeeklyCapacityHours] = useState<number | string>(40);
  const [targetUtilizationPercent, setTargetUtilizationPercent] = useState<number | string>(80);
  const [avatarUrl, setAvatarUrl] = useState('');
  const [bio, setBio] = useState('');

  const [errors, setErrors] = useState<Record<string, string>>({});
  const [serverError, setServerError] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);

  useEffect(() => {
    if (open) {
      setServerError(null);
      setErrors({});
      if (employeeToEdit) {
        setFullName(employeeToEdit.fullName || '');
        setEmail(employeeToEdit.email || '');
        setDepartment(employeeToEdit.department || '');
        setJobTitle(employeeToEdit.jobTitle || '');
        setSeniorityRank((employeeToEdit.seniorityRank as SeniorityRank) || 'Mid');
        setRole((employeeToEdit.role as AppRole) || 'Employee');
        if ('avatarUrl' in employeeToEdit && employeeToEdit.avatarUrl) {
          setAvatarUrl(employeeToEdit.avatarUrl);
        } else {
          setAvatarUrl('');
        }
        if ('bio' in employeeToEdit && employeeToEdit.bio) {
          setBio(employeeToEdit.bio);
        } else {
          setBio('');
        }

        if ('annualBaseSalary' in employeeToEdit) {
          const comp = employeeToEdit as EmployeeCompensationDto;
          setAnnualBaseSalary(comp.annualBaseSalary ?? 0);
          setHourlyCostRate(comp.hourlyCostRate ?? 0);
          setHourlyBillableRate(comp.hourlyBillableRate ?? 0);
          setWeeklyCapacityHours(comp.weeklyCapacityHours ?? 40);
          setTargetUtilizationPercent(comp.targetUtilizationPercent ?? 80);
        } else {
          // If editing from directory where salary info isn't on the summary DTO
          setAnnualBaseSalary(75000);
          setHourlyCostRate(36.06);
          setHourlyBillableRate(120);
          setWeeklyCapacityHours(40);
          setTargetUtilizationPercent(80);
        }
      } else {
        // Reset defaults for creation
        setFullName('');
        setEmail('');
        setDepartment('');
        setJobTitle('');
        setSeniorityRank('Mid');
        setRole('Employee');
        setAnnualBaseSalary(75000);
        setHourlyCostRate(36.06);
        setHourlyBillableRate(120);
        setWeeklyCapacityHours(40);
        setTargetUtilizationPercent(80);
        setAvatarUrl('');
        setBio('');
      }
    }
  }, [open, employeeToEdit]);

  const handleAutoCalcCostRate = () => {
    const salary = Number(annualBaseSalary) || 0;
    const capacity = Number(weeklyCapacityHours) || 40;
    const totalWorkingHoursPerYear = capacity * 52;
    if (totalWorkingHoursPerYear > 0) {
      const calculated = parseFloat((salary / totalWorkingHoursPerYear).toFixed(2));
      setHourlyCostRate(calculated);
    }
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setServerError(null);

    const formData = {
      fullName: fullName.trim(),
      email: email.trim(),
      department: department.trim(),
      jobTitle: jobTitle.trim(),
      annualBaseSalary: Number(annualBaseSalary),
      hourlyCostRate: Number(hourlyCostRate),
      hourlyBillableRate: Number(hourlyBillableRate),
      weeklyCapacityHours: Number(weeklyCapacityHours),
      targetUtilizationPercent: Number(targetUtilizationPercent),
      bio: bio.trim() || undefined,
    };

    const result = employeeManagementSchema.safeParse(formData);
    if (!result.success) {
      const fieldErrors: Record<string, string> = {};
      result.error.issues.forEach((issue) => {
        const path = issue.path[0] as string;
        if (!fieldErrors[path]) {
          fieldErrors[path] = issue.message;
        }
      });
      setErrors(fieldErrors);
      return;
    }

    setErrors({});
    setIsSubmitting(true);

    try {
      const payload: CreateEmployeeRequestDto = {
        fullName: formData.fullName,
        email: formData.email,
        department: formData.department,
        jobTitle: formData.jobTitle,
        seniorityRank,
        role,
        annualBaseSalary: formData.annualBaseSalary,
        hourlyCostRate: formData.hourlyCostRate,
        hourlyBillableRate: formData.hourlyBillableRate,
        weeklyCapacityHours: formData.weeklyCapacityHours,
        targetUtilizationPercent: formData.targetUtilizationPercent,
        avatarUrl: avatarUrl.trim() || undefined,
        bio: formData.bio,
      };

      if (isEditing && employeeToEdit) {
        const editId = 'employeeId' in employeeToEdit ? employeeToEdit.employeeId : employeeToEdit.id;
        const updatePayload: UpdateEmployeeRequestDto = { ...payload };
        await managementApi.updateEmployee(editId, updatePayload);
      } else {
        await managementApi.createEmployee(payload);
      }
      onSuccess();
      onClose();
    } catch (err: any) {
      const msg = err.response?.data?.message || err.message || 'Operation failed';
      setServerError(msg);
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <Dialog open={open} onClose={isSubmitting ? undefined : onClose} maxWidth="md" fullWidth>
      <form onSubmit={handleSubmit} noValidate>
        <DialogTitle>{isEditing ? 'Edit Employee' : 'Add New Employee'}</DialogTitle>
        <DialogContent dividers>
          {serverError && (
            <Alert severity="error" sx={{ mb: 2 }}>
              {serverError}
            </Alert>
          )}

          <Grid container spacing={2}>
            {/* Identity & Basic Info */}
            <Grid item xs={12} sm={6}>
              <TextField
                label="Full Name"
                fullWidth
                required
                value={fullName}
                onChange={(e) => setFullName(e.target.value)}
                error={Boolean(errors.fullName)}
                helperText={errors.fullName}
                disabled={isSubmitting}
              />
            </Grid>
            <Grid item xs={12} sm={6}>
              <TextField
                label="Email"
                type="email"
                fullWidth
                required
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                error={Boolean(errors.email)}
                helperText={errors.email}
                disabled={isSubmitting}
              />
            </Grid>
            <Grid item xs={12} sm={6}>
              <TextField
                label="Department"
                fullWidth
                required
                value={department}
                onChange={(e) => setDepartment(e.target.value)}
                error={Boolean(errors.department)}
                helperText={errors.department}
                disabled={isSubmitting}
              />
            </Grid>
            <Grid item xs={12} sm={6}>
              <TextField
                label="Job Title"
                fullWidth
                required
                value={jobTitle}
                onChange={(e) => setJobTitle(e.target.value)}
                error={Boolean(errors.jobTitle)}
                helperText={errors.jobTitle}
                disabled={isSubmitting}
              />
            </Grid>

            {/* Seniority Rank & Role */}
            <Grid item xs={12} sm={6}>
              <FormControl fullWidth required>
                <InputLabel id="seniority-rank-label">Seniority Rank</InputLabel>
                <Select
                  labelId="seniority-rank-label"
                  label="Seniority Rank"
                  value={seniorityRank}
                  onChange={(e) => setSeniorityRank(e.target.value as SeniorityRank)}
                  disabled={isSubmitting}
                >
                  <MenuItem value="Junior">Junior</MenuItem>
                  <MenuItem value="Mid">Mid</MenuItem>
                  <MenuItem value="Senior">Senior</MenuItem>
                  <MenuItem value="Lead">Lead</MenuItem>
                  <MenuItem value="Principal">Principal</MenuItem>
                </Select>
              </FormControl>
            </Grid>
            <Grid item xs={12} sm={6}>
              <FormControl fullWidth required>
                <InputLabel id="system-role-label">System Role</InputLabel>
                <Select
                  labelId="system-role-label"
                  label="System Role"
                  value={role}
                  onChange={(e) => setRole(e.target.value as AppRole)}
                  disabled={isSubmitting}
                >
                  <MenuItem value="Employee">Employee</MenuItem>
                  <MenuItem value="TeamLead">TeamLead</MenuItem>
                  <MenuItem value="HigherManagement">HigherManagement</MenuItem>
                </Select>
              </FormControl>
            </Grid>

            {/* Compensation & Capacity */}
            <Grid item xs={12} sm={4}>
              <TextField
                label="Annual Base Salary"
                type="number"
                fullWidth
                required
                value={annualBaseSalary}
                onChange={(e) => setAnnualBaseSalary(e.target.value)}
                InputProps={{
                  startAdornment: <InputAdornment position="start">$</InputAdornment>,
                }}
                error={Boolean(errors.annualBaseSalary)}
                helperText={errors.annualBaseSalary}
                disabled={isSubmitting}
              />
            </Grid>
            <Grid item xs={12} sm={4}>
              <TextField
                label="Hourly Cost Rate"
                type="number"
                fullWidth
                required
                value={hourlyCostRate}
                onChange={(e) => setHourlyCostRate(e.target.value)}
                InputProps={{
                  startAdornment: <InputAdornment position="start">$</InputAdornment>,
                  endAdornment: (
                    <InputAdornment position="end">
                      <Tooltip title="Auto-calculate based on Salary / (Capacity * 52)">
                        <IconButton size="small" onClick={handleAutoCalcCostRate} edge="end">
                          <CalculateIcon fontSize="small" />
                        </IconButton>
                      </Tooltip>
                    </InputAdornment>
                  ),
                }}
                error={Boolean(errors.hourlyCostRate)}
                helperText={errors.hourlyCostRate}
                disabled={isSubmitting}
              />
            </Grid>
            <Grid item xs={12} sm={4}>
              <TextField
                label="Hourly Billable Rate"
                type="number"
                fullWidth
                required
                value={hourlyBillableRate}
                onChange={(e) => setHourlyBillableRate(e.target.value)}
                InputProps={{
                  startAdornment: <InputAdornment position="start">$</InputAdornment>,
                }}
                error={Boolean(errors.hourlyBillableRate)}
                helperText={errors.hourlyBillableRate}
                disabled={isSubmitting}
              />
            </Grid>

            <Grid item xs={12} sm={6}>
              <TextField
                label="Weekly Capacity (Hours)"
                type="number"
                fullWidth
                required
                value={weeklyCapacityHours}
                onChange={(e) => setWeeklyCapacityHours(e.target.value)}
                InputProps={{
                  endAdornment: <InputAdornment position="end">hrs/wk</InputAdornment>,
                }}
                error={Boolean(errors.weeklyCapacityHours)}
                helperText={errors.weeklyCapacityHours}
                disabled={isSubmitting}
              />
            </Grid>
            <Grid item xs={12} sm={6}>
              <TextField
                label="Target Utilization (%)"
                type="number"
                fullWidth
                required
                value={targetUtilizationPercent}
                onChange={(e) => setTargetUtilizationPercent(e.target.value)}
                InputProps={{
                  endAdornment: <InputAdornment position="end">%</InputAdornment>,
                }}
                error={Boolean(errors.targetUtilizationPercent)}
                helperText={errors.targetUtilizationPercent}
                disabled={isSubmitting}
              />
            </Grid>

            {/* Profile fields */}
            <Grid item xs={12}>
              <TextField
                label="Avatar URL (Optional)"
                fullWidth
                value={avatarUrl}
                onChange={(e) => setAvatarUrl(e.target.value)}
                disabled={isSubmitting}
              />
            </Grid>
            <Grid item xs={12}>
              <TextField
                label="Bio (Optional)"
                multiline
                rows={2}
                fullWidth
                value={bio}
                onChange={(e) => setBio(e.target.value)}
                error={Boolean(errors.bio)}
                helperText={errors.bio}
                disabled={isSubmitting}
              />
            </Grid>
          </Grid>
        </DialogContent>
        <DialogActions sx={{ px: 3, py: 2 }}>
          <Button onClick={onClose} disabled={isSubmitting}>
            Cancel
          </Button>
          <Button
            type="submit"
            variant="contained"
            disabled={isSubmitting}
            startIcon={isSubmitting ? <CircularProgress size={18} /> : undefined}
          >
            {isEditing ? 'Save Changes' : 'Create Employee'}
          </Button>
        </DialogActions>
      </form>
    </Dialog>
  );
};
