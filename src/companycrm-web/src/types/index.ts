// Enums matching CompanyCRM.Domain.Enums
export type AppRole = 'Employee' | 'TeamLead' | 'HigherManagement';
export type SeniorityRank = 'Junior' | 'Mid' | 'Senior' | 'Lead' | 'Principal';
export type ProjectRole = 'Member' | 'Lead' | 'Manager';
export type WorkItemType = 'Task' | 'Bug' | 'Story';
export type WorkItemStatus =
  | 'Draft'
  | 'PendingReview'
  | 'ClarificationRequested'
  | 'Denied'
  | 'Backlog'
  | 'Todo'
  | 'InProgress'
  | 'InVerify'
  | 'Done';
export type WorkItemPriority = 'Low' | 'Medium' | 'High' | 'Critical';
export type ReviewStatus = 'NotRequired' | 'Pending' | 'Approved' | 'ClarificationNeeded' | 'Denied';
export type ExternalSourceType = 'None' | 'AzureDevOps' | 'Jira' | 'GitHub';
export type SyncOrigin = 'LocalCrm' | 'ExternalWebhook' | 'BackgroundReconciliation';
export type SyncConflictType = 'DuplicateCandidate' | 'ConcurrentUpdate';
export type ConflictStatus = 'Pending' | 'Resolved';
export type ConflictResolutionAction = 'Unresolved' | 'Merge' | 'KeepBoth' | 'Skip';
export type WeeklyPlanStatus = 'Draft' | 'Submitted' | 'Completed' | 'Approved';

// Generic Paged Result
export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

// Auth DTOs
export interface LoginRequest {
  email: string;
  password?: string;
}

export interface AuthResponseDto {
  token: string;
  expiresAt: string;
  user: EmployeeProfileDto;
  role: string;
  permissions: string[];
}

export interface CurrentUserDto {
  id: string;
  fullName: string;
  email: string;
  avatarUrl: string | null;
  department: string;
  jobTitle: string;
  seniorityRank: SeniorityRank;
  role: AppRole;
  permissions: string[];
  layoutPreferencesJson: string | null;
}

// Employee DTOs
export interface EmployeeSkillDto {
  id: string;
  skillName: string;
  category: string;
  proficiencyLevel: number;
  yearsOfExperience: number;
  lastEvaluatedAt: string | null;
}

export interface EmployeeProfileDto {
  id: string;
  fullName: string;
  email: string;
  avatarUrl: string | null;
  department: string;
  jobTitle: string;
  seniorityRank: SeniorityRank;
  role: AppRole;
  bio: string | null;
  layoutPreferencesJson: string | null;
  skills: EmployeeSkillDto[];
}

export interface EmployeeDirectoryDto {
  id: string;
  fullName: string;
  email: string;
  avatarUrl: string | null;
  department: string;
  jobTitle: string;
  seniorityRank: SeniorityRank;
  role: AppRole;
  bio: string | null;
  skills: EmployeeSkillDto[];
}

export interface UpdateProfileRequestDto {
  bio?: string | null;
  layoutPreferencesJson?: string | null;
}

export interface UpsertSkillRequestDto {
  skillName: string;
  category: string;
  proficiencyLevel: number;
  yearsOfExperience?: number;
}

export interface CreateEmployeeRequestDto {
  fullName: string;
  email: string;
  department: string;
  jobTitle: string;
  seniorityRank: SeniorityRank;
  role: AppRole;
  annualBaseSalary: number;
  hourlyCostRate: number;
  hourlyBillableRate: number;
  weeklyCapacityHours: number;
  targetUtilizationPercent: number;
  avatarUrl?: string | null;
  bio?: string | null;
}

export interface UpdateEmployeeRequestDto {
  fullName: string;
  email: string;
  department: string;
  jobTitle: string;
  seniorityRank: SeniorityRank;
  role: AppRole;
  annualBaseSalary: number;
  hourlyCostRate: number;
  hourlyBillableRate: number;
  weeklyCapacityHours: number;
  targetUtilizationPercent: number;
  avatarUrl?: string | null;
  bio?: string | null;
}

// Project DTOs
export interface ProjectDto {
  id: string;
  code: string;
  name: string;
  description: string | null;
  isActive: boolean;
  externalSource: ExternalSourceType;
  externalOrgUrl: string | null;
  externalProjectName: string | null;
  isSyncEnabled: boolean;
  isSimulatedMode: boolean;
  createdAt: string;
  updatedAt: string | null;
  memberCount: number;
}

export interface CreateProjectDto {
  code: string;
  name: string;
  description?: string | null;
  externalSource?: ExternalSourceType;
  externalOrgUrl?: string | null;
  externalProjectName?: string | null;
  isSyncEnabled?: boolean;
  isSimulatedMode?: boolean;
}

export interface UpdateProjectDto {
  name: string;
  description?: string | null;
  isActive: boolean;
}

export interface ProjectMemberDto {
  id: string;
  projectId: string;
  employeeId: string;
  employeeName: string;
  employeeEmail: string;
  role: ProjectRole;
  joinedAt: string;
}

export interface AddProjectMemberDto {
  employeeId: string;
  role: ProjectRole;
}

export interface ProjectExternalConfigDto {
  projectId: string;
  projectName?: string;
  externalSource: ExternalSourceType;
  externalOrgUrl: string | null;
  externalProjectName: string | null;
  hasPatConfigured?: boolean;
  personalAccessTokenMasked?: string | null;
  isSyncEnabled: boolean;
  isSimulatedMode: boolean;
  lastSyncRunAt?: string | null;
  lastSyncedAt?: string | null;
}

export interface UpdateProjectExternalConfigDto {
  externalSource?: ExternalSourceType;
  externalOrgUrl?: string | null;
  externalProjectName?: string | null;
  externalPat?: string | null;
  personalAccessToken?: string | null;
  isSyncEnabled?: boolean;
  isSimulatedMode?: boolean;
}

// Work Item DTOs
export interface WorkItemDto {
  id: string;
  projectId: string;
  projectCode: string;
  code: string;
  title: string;
  description: string | null;
  type: WorkItemType;
  status: WorkItemStatus;
  priority: WorkItemPriority;
  creatorId: string;
  createdById?: string;
  creatorName: string;
  createdByName?: string;
  assigneeId: string | null;
  assigneeName: string | null;
  reviewerId: string | null;
  reviewerName: string | null;
  reviewStatus: ReviewStatus;
  reviewComment: string | null;
  reviewLatestComment?: string | null;
  reviewedAt: string | null;
  estimatedHours: number | null;
  storyPoints: number | null;
  boardPosition: number;
  sprintName: string | null;
  externalId: string | null;
  externalUrl: string | null;
  lastSyncedContentHash: string | null;
  lastSyncedAt: string | null;
  lastSyncOrigin: SyncOrigin;
  isLockedOnBoard: boolean;
  isTimerRunningForCurrentUser?: boolean;
  totalLoggedMinutes: number;
  billableMinutes: number;
  nonBillableMinutes: number;
  createdAt: string;
  updatedAt: string | null;
}

export interface CreateWorkItemDto {
  projectId: string;
  title: string;
  description?: string | null;
  type: WorkItemType;
  priority?: WorkItemPriority;
  assigneeId?: string | null;
  reviewerId?: string | null;
  estimatedHours?: number | null;
  storyPoints?: number | null;
  sprintName?: string | null;
}

export interface UpdateWorkItemDto {
  title: string;
  description?: string | null;
  type: WorkItemType;
  priority?: WorkItemPriority;
  assigneeId?: string | null;
  reviewerId?: string | null;
  estimatedHours?: number | null;
  storyPoints?: number | null;
  sprintName?: string | null;
}

export interface KanbanTransitionDto {
  status: WorkItemStatus;
  boardPosition?: number;
}

export interface StartTimerRequestDto {
  isBillable?: boolean;
}

export interface StopTimerRequestDto {
  notes?: string | null;
  isBillable?: boolean;
}

export interface CreateManualTimeEntryDto {
  durationMinutes: number;
  isBillable: boolean;
  notes?: string | null;
  loggedDate: string;
}

export interface TimeEntryDto {
  id: string;
  workItemId: string;
  workItemCode: string;
  workItemTitle: string;
  employeeId: string;
  employeeName: string;
  durationMinutes: number;
  isBillable: boolean;
  isRunning: boolean;
  startedAt: string;
  endedAt: string | null;
  loggedDate: string;
  notes: string | null;
}

export interface WorkItemHistoryDto {
  id: string;
  workItemId: string;
  changedById: string;
  changedByName: string;
  changedAt: string;
  action?: string;
  changeType?: string;
  fieldName: string | null;
  oldValue: string | null;
  newValue: string | null;
  note?: string | null;
  notes?: string | null;
}

// Review DTOs
export interface WorkItemReviewQueueDto {
  id: string;
  workItemId?: string;
  projectId: string;
  projectCode: string;
  projectName: string;
  code: string;
  workItemCode?: string;
  title: string;
  workItemTitle?: string;
  descriptionPreview?: string | null;
  type: WorkItemType;
  priority: WorkItemPriority;
  creatorId: string;
  createdById?: string;
  creatorName: string;
  createdByName?: string;
  reviewerId: string | null;
  reviewerName: string | null;
  reviewStatus: ReviewStatus;
  reviewComment: string | null;
  latestComment?: string | null;
  requestedAt: string;
  createdAt?: string;
}

export interface ApproveReviewRequestDto {
  startingStatus?: WorkItemStatus;
  approvalNote?: string | null;
}

export interface ClarifyReviewRequestDto {
  comment: string;
}

export interface DenyReviewRequestDto {
  reason: string;
}

// Sync DTOs
export interface SyncResultDto {
  success?: boolean;
  isSuccess?: boolean;
  workItemId?: string;
  externalId: string | null;
  externalUrl: string | null;
  contentHash: string | null;
  errorMessage?: string | null;
  message?: string;
  origin?: SyncOrigin;
}

export interface WebhookProcessingResultDto {
  status: string;
  workItemId: string | null;
  externalId: string | null;
  message: string;
}

export interface ReconciliationResultDto {
  projectId: string | null;
  totalItemsChecked: number;
  updatedCount: number;
  conflictsCount: number;
  skippedCount: number;
  errors: string[];
  startedAt: string;
  completedAt: string;
  fetchedCount?: number;
  conflictsDetectedCount?: number;
  isSuccess?: boolean;
  message?: string;
}

export interface SyncConflictDto {
  id: string;
  projectId: string;
  projectName?: string;
  localWorkItemId: string | null;
  localWorkItemCode: string | null;
  localTitle: string | null;
  localStatus?: string | null;
  localAssigneeName?: string | null;
  externalSource?: ExternalSourceType;
  externalId: string;
  externalTitle: string | null;
  externalDescription?: string | null;
  externalStatus: string | null;
  externalAssigneeEmail: string | null;
  conflictType: SyncConflictType;
  status: ConflictStatus;
  resolutionAction: ConflictResolutionAction;
  resolutionNotes: string | null;
  similarityScore: number;
  detectedAt: string;
  resolvedAt: string | null;
  resolvedByName?: string | null;
}

export interface ResolveConflictRequestDto {
  action: ConflictResolutionAction;
  resolutionNotes?: string | null;
}

export interface ConflictResolutionResultDto {
  conflictId: string;
  action: ConflictResolutionAction;
  resolved?: boolean;
  isSuccess?: boolean;
  resolvedWorkItemId?: string | null;
  workItemId?: string | null;
  message: string | null;
}

// Planning DTOs
export interface WeeklyPlanDto {
  id: string;
  employeeId: string;
  employeeName: string;
  projectId: string | null;
  projectName: string | null;
  weekStartDate: string;
  goalsMarkdown: string;
  focusAreasJson: string | null;
  keyDeliverables: string | null;
  status: WeeklyPlanStatus;
  submittedAt: string | null;
  approvedAt: string | null;
  approvedByName: string | null;
  createdAt: string;
  updatedAt: string | null;
}

export interface CreateOrUpdateWeeklyPlanDto {
  weekStartDate: string;
  projectId?: string | null;
  goalsMarkdown: string;
  focusAreasJson?: string | null;
  keyDeliverables?: string | null;
  status?: WeeklyPlanStatus;
}

export interface DailyPlanItemDto {
  id: string;
  workItemId: string | null;
  workItemCode: string | null;
  workItemTitle: string | null;
  customTaskTitle: string | null;
  plannedHours: number;
  goalNote: string | null;
  isCompleted: boolean;
  sortOrder: number;
}

export interface DailyPlanDto {
  id: string;
  employeeId: string;
  employeeName: string;
  planDate: string;
  submittedAt: string;
  isSubmittedOnTime: boolean;
  blockers: string | null;
  summaryNotes: string | null;
  totalPlannedHours: number;
  items: DailyPlanItemDto[];
}

export interface SubmitDailyPlanItemDto {
  workItemId?: string | null;
  customTaskTitle?: string | null;
  plannedHours: number;
  goalNote?: string | null;
  isCompleted?: boolean;
  sortOrder?: number;
}

export interface SubmitDailyPlanRequestDto {
  planDate: string;
  blockers?: string | null;
  summaryNotes?: string | null;
  items: SubmitDailyPlanItemDto[];
  projectId?: string | null;
}

export interface PlanningCutoffConfigDto {
  id: string;
  projectId: string | null;
  projectName: string | null;
  cutoffTimeLocal: string;
  timeZoneId: string;
  isReminderEnabled?: boolean;
  isReminderEmailEnabled?: boolean;
  excludedDaysOfWeekJson?: string;
  excludedDaysOfWeek?: (number | string)[];
  updatedAt: string;
}

export interface UpdatePlanningCutoffConfigDto {
  projectId?: string | null;
  cutoffTimeLocal: string;
  timeZoneId: string;
  isReminderEnabled?: boolean;
  isReminderEmailEnabled?: boolean;
  excludedDaysOfWeekJson?: string;
  excludedDaysOfWeek?: (number | string)[];
}

export interface CutoffExecutionSummaryDto {
  status: string;
  checkedDate?: string;
  cutoffTime?: string;
  timeZoneId?: string;
  remindersSent?: number;
  notifiedEmployeeEmails?: string[];
  executionDurationMs?: number;
  errors?: string[];
  executionTime?: string;
  targetDate?: string;
  employeesCheckedCount?: number;
  reminderEmailsSentCount?: number;
  missingPlanEmployeeIds?: string[];
  missingPlanEmployeeNames?: string[];
}

// Management DTOs
export interface EmployeeCompensationDto {
  employeeId: string;
  fullName: string;
  email: string;
  department: string;
  jobTitle: string;
  seniorityRank: SeniorityRank;
  role: AppRole;
  annualBaseSalary: number;
  hourlyCostRate: number;
  hourlyBillableRate: number;
  weeklyCapacityHours: number;
  targetUtilizationPercent: number;
  actualUtilizationPercent: number;
  dailyPlanCompliancePercent: number;
  onTimeSubmissionPercent: number;
  totalBillableHoursLogged: number;
  totalNonBillableHoursLogged: number;
}

export interface ProjectProfitabilityDto {
  projectId: string;
  projectName: string;
  projectCode: string;
  startDate?: string | null;
  endDate?: string | null;
  totalLoggedHours: number;
  totalBillableHours: number;
  totalNonBillableHours: number;
  totalRevenue: number;
  totalInternalCost: number;
  grossProfit: number;
  grossMarginPercent: number;
  totalHours?: number;
  totalClientRevenue?: number;
  totalInternalLaborCost?: number;
  employeeBreakdowns: EmployeeProjectProfitabilityDto[];
}

export interface EmployeeProjectProfitabilityDto {
  employeeId: string;
  fullName: string;
  employeeName?: string;
  department?: string;
  hourlyCostRate: number;
  hourlyBillableRate: number;
  loggedHours: number;
  billableHours: number;
  internalCost: number;
  revenue: number;
  billedRevenue?: number;
  grossProfit: number;
  grossMarginPercent?: number;
}

export type ProjectEmployeeFinancialDto = EmployeeProjectProfitabilityDto;

export interface SprintVelocityDto {
  sprintName: string;
  plannedPoints: number;
  completedPoints: number;
  startDate?: string;
  endDate?: string;
}

export interface HoursComparisonDto {
  workItemId: string;
  workItemCode: string;
  workItemTitle: string;
  estimatedHours: number;
  billableHours: number;
  nonBillableHours: number;
  totalLoggedHours: number;
  varianceHours: number;
}

export interface CompletionRateDto {
  projectId: string;
  projectName?: string;
  totalWorkItems: number;
  completedWorkItems: number;
  overallCompletionPercent: number;
  statusCounts: Record<string, number>;
  typeCounts: Record<string, number>;
  totalItems?: number;
  doneItems?: number;
  inProgressItems?: number;
  inVerifyItems?: number;
  todoItems?: number;
  backlogItems?: number;
}

export interface BurndownDayDto {
  dayIndex: number;
  date: string;
  idealRemainingPoints: number;
  actualRemainingPoints: number;
}

export interface BurndownDto {
  projectId: string;
  sprintName: string;
  totalStoryPoints: number;
  days: BurndownDayDto[];
}

// SignalR Events
export interface ReviewQueueEventDto {
  workItemId: string;
  workItemCode: string;
  workItemTitle: string;
  projectId?: string;
  reviewerId: string | null;
  reviewerName?: string | null;
  action: string;
  reviewStatus?: ReviewStatus;
  comment: string | null;
  timestamp: string;
}

export interface SyncEventDto {
  projectId: string;
  workItemId?: string | null;
  externalId?: string | null;
  status: string;
  eventType?: string;
  message: string;
  timestamp: string;
}

export interface DailyPlanSubmittedDto {
  employeeId: string;
  employeeName: string;
  planDate: string;
  isOnTime: boolean;
  submittedAt: string;
  itemsCount: number;
}
