import { PlanningCutoffConfigDto, UpdatePlanningCutoffConfigDto } from '../../types';

export interface WeekdayOption {
  index: number; // System.DayOfWeek: 0 = Sunday .. 6 = Saturday
  label: string;
}

export const DAYS_OF_WEEK: WeekdayOption[] = [
  { index: 1, label: 'Monday' },
  { index: 2, label: 'Tuesday' },
  { index: 3, label: 'Wednesday' },
  { index: 4, label: 'Thursday' },
  { index: 5, label: 'Friday' },
  { index: 6, label: 'Saturday' },
  { index: 0, label: 'Sunday' },
];

export const DAY_NAME_TO_INDEX: Record<string, number> = {
  sunday: 0,
  monday: 1,
  tuesday: 2,
  wednesday: 3,
  thursday: 4,
  friday: 5,
  saturday: 6,
};

export const DEFAULT_EXCLUDED_DAY_INDICES = [0, 6];

function normalizeDayToken(token: unknown): number | null {
  if (typeof token === 'number' && Number.isInteger(token) && token >= 0 && token <= 6) {
    return token;
  }
  if (typeof token === 'string') {
    const trimmed = token.trim();
    if (/^[0-6]$/.test(trimmed)) {
      return Number(trimmed);
    }
    const mapped = DAY_NAME_TO_INDEX[trimmed.toLowerCase()];
    if (mapped !== undefined) {
      return mapped;
    }
  }
  return null;
}

export function parseExcludedDaysToIndices(
  cfg?: Partial<Pick<PlanningCutoffConfigDto, 'excludedDaysOfWeekJson' | 'excludedDaysOfWeek'>> | null
): number[] {
  if (!cfg) return [...DEFAULT_EXCLUDED_DAY_INDICES];

  let rawArray: unknown = null;
  if (typeof cfg.excludedDaysOfWeekJson === 'string' && cfg.excludedDaysOfWeekJson.trim() !== '') {
    try {
      rawArray = JSON.parse(cfg.excludedDaysOfWeekJson);
    } catch {
      return [...DEFAULT_EXCLUDED_DAY_INDICES];
    }
  } else if (Array.isArray(cfg.excludedDaysOfWeek)) {
    rawArray = cfg.excludedDaysOfWeek;
  } else {
    return [...DEFAULT_EXCLUDED_DAY_INDICES];
  }

  if (!Array.isArray(rawArray)) {
    return [...DEFAULT_EXCLUDED_DAY_INDICES];
  }

  const normalized = new Set<number>();
  for (const item of rawArray) {
    const idx = normalizeDayToken(item);
    if (idx !== null) {
      normalized.add(idx);
    }
  }
  return Array.from(normalized).sort((a, b) => a - b);
}

export function serializeExcludedDays(indices: number[]): string {
  const valid = Array.from(
    new Set(indices.filter((n) => Number.isInteger(n) && n >= 0 && n <= 6))
  ).sort((a, b) => a - b);
  return JSON.stringify(valid);
}

export type CutoffScopeMode = 'global' | 'project';

export function resolveTargetCutoffProjectId(
  scopeMode: CutoffScopeMode,
  activeProjectId?: string | null
): string | null {
  if (scopeMode === 'project' && typeof activeProjectId === 'string') {
    const trimmed = activeProjectId.trim();
    if (trimmed.length > 0) {
      return trimmed;
    }
  }
  return null;
}

export function isInheritedGlobalFallback(
  scopeMode: CutoffScopeMode,
  loadedConfigProjectId?: string | null
): boolean {
  return scopeMode === 'project' && !loadedConfigProjectId;
}

export function buildUpdateCutoffConfigDto(params: {
  projectId?: string | null;
  cutoffTimeLocal: string;
  timeZoneId: string;
  isReminderEnabled: boolean;
  excludedDayIndices: number[];
}): UpdatePlanningCutoffConfigDto {
  const normalizedDays = Array.from(
    new Set(params.excludedDayIndices.filter((n) => Number.isInteger(n) && n >= 0 && n <= 6))
  ).sort((a, b) => a - b);

  const normalizedProjectId =
    typeof params.projectId === 'string' && params.projectId.trim() !== ''
      ? params.projectId.trim()
      : null;

  return {
    projectId: normalizedProjectId,
    cutoffTimeLocal: params.cutoffTimeLocal.trim(),
    timeZoneId: params.timeZoneId.trim(),
    isReminderEnabled: params.isReminderEnabled,
    isReminderEmailEnabled: params.isReminderEnabled,
    excludedDaysOfWeekJson: JSON.stringify(normalizedDays),
    excludedDaysOfWeek: normalizedDays,
  };
}
