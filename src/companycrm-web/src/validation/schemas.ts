import { z } from 'zod';
import { LIMITS } from './limits';

export * from './limits';

export const projectSchema = z.object({
  code: z
    .string()
    .trim()
    .min(LIMITS.projectCodeMin, `Project code must be at least ${LIMITS.projectCodeMin} characters.`)
    .max(LIMITS.projectCodeMax, `Project code cannot exceed ${LIMITS.projectCodeMax} characters.`)
    .regex(/^[A-Za-z0-9]+$/, 'Project code must be alphanumeric (letters and numbers only).'),
  name: z
    .string()
    .trim()
    .min(1, 'Project name is required.')
    .max(LIMITS.projectNameMax, `Project name cannot exceed ${LIMITS.projectNameMax} characters.`),
  description: z
    .string()
    .trim()
    .max(LIMITS.projectDescriptionMax, `Description cannot exceed ${LIMITS.projectDescriptionMax} characters.`)
    .optional()
    .or(z.literal('')),
});

export const workItemSchema = z.object({
  title: z
    .string()
    .trim()
    .min(1, 'Title is required.')
    .max(LIMITS.workItemTitleMax, `Title cannot exceed ${LIMITS.workItemTitleMax} characters.`),
  description: z
    .string()
    .trim()
    .max(LIMITS.workItemDescriptionMax, `Description cannot exceed ${LIMITS.workItemDescriptionMax} characters.`)
    .optional()
    .or(z.literal('')),
  sprintName: z
    .string()
    .trim()
    .max(LIMITS.sprintNameMax, `Sprint name cannot exceed ${LIMITS.sprintNameMax} characters.`)
    .optional()
    .or(z.literal('')),
  estimatedHours: z
    .number()
    .min(LIMITS.estimatedHoursMin, 'Estimated hours must be non-negative.')
    .max(LIMITS.estimatedHoursMax, `Estimated hours cannot exceed ${LIMITS.estimatedHoursMax}.`)
    .optional()
    .nullable(),
  storyPoints: z
    .number()
    .int('Story points must be an integer.')
    .min(LIMITS.storyPointsMin, 'Story points must be non-negative.')
    .max(LIMITS.storyPointsMax, `Story points cannot exceed ${LIMITS.storyPointsMax}.`)
    .optional()
    .nullable(),
});

export const manualTimeEntrySchema = z.object({
  durationMinutes: z
    .number()
    .int('Duration must be a whole number of minutes.')
    .min(LIMITS.timeEntryDurationMin, `Duration must be at least ${LIMITS.timeEntryDurationMin} minute.`)
    .max(LIMITS.timeEntryDurationMax, `Duration cannot exceed ${LIMITS.timeEntryDurationMax} minutes (24 hours).`),
  notes: z
    .string()
    .trim()
    .max(LIMITS.timeEntryNotesMax, `Notes cannot exceed ${LIMITS.timeEntryNotesMax} characters.`)
    .optional()
    .or(z.literal('')),
});

export const employeeManagementSchema = z.object({
  fullName: z
    .string()
    .trim()
    .min(1, 'Full name is required.')
    .max(LIMITS.fullNameMax, `Full name cannot exceed ${LIMITS.fullNameMax} characters.`),
  email: z
    .string()
    .trim()
    .min(1, 'Email is required.')
    .max(LIMITS.emailMax, `Email cannot exceed ${LIMITS.emailMax} characters.`)
    .email('Please enter a valid email address.'),
  department: z
    .string()
    .trim()
    .min(1, 'Department is required.')
    .max(LIMITS.departmentMax, `Department cannot exceed ${LIMITS.departmentMax} characters.`),
  jobTitle: z
    .string()
    .trim()
    .min(1, 'Job title is required.')
    .max(LIMITS.jobTitleMax, `Job title cannot exceed ${LIMITS.jobTitleMax} characters.`),
  annualBaseSalary: z
    .number()
    .min(LIMITS.salaryMin, 'Base salary must be non-negative.'),
  hourlyCostRate: z
    .number()
    .min(LIMITS.rateMin, 'Hourly cost rate must be non-negative.'),
  hourlyBillableRate: z
    .number()
    .min(LIMITS.rateMin, 'Hourly billable rate must be non-negative.'),
  weeklyCapacityHours: z
    .number()
    .min(LIMITS.capacityMin, `Weekly capacity must be at least ${LIMITS.capacityMin} hour.`)
    .max(LIMITS.capacityMax, `Weekly capacity cannot exceed ${LIMITS.capacityMax} hours.`),
  targetUtilizationPercent: z
    .number()
    .min(LIMITS.utilizationMin, 'Target utilization must be at least 0%.')
    .max(LIMITS.utilizationMax, 'Target utilization cannot exceed 100%.'),
  bio: z
    .string()
    .trim()
    .max(LIMITS.bioMax, `Bio cannot exceed ${LIMITS.bioMax} characters.`)
    .optional()
    .or(z.literal('')),
});
