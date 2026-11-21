export type ThemeMode = 'light' | 'dark';

export function resolveSavedThemeMode(saved: string | null | undefined): ThemeMode {
  return saved === 'dark' || saved === 'light' ? saved : 'light';
}

export function getNextThemeMode(current: ThemeMode): ThemeMode {
  return current === 'light' ? 'dark' : 'light';
}

export function resolveActiveProject<T extends { id: string }>(
  projects: T[],
  savedId: string | null | undefined
): T | null {
  return projects.find((p) => p.id === savedId) || projects[0] || null;
}

export function dispatchCallbackSafely<T>(
  callbacks: Iterable<(payload: T) => void>,
  payload: T,
  onError?: (err: unknown) => void
): void {
  for (const cb of callbacks) {
    try {
      cb(payload);
    } catch (e) {
      if (onError) {
        onError(e);
      }
    }
  }
}

export function canAccessRoute(
  role: string | null | undefined,
  permissions: string[] | undefined,
  path: string
): boolean {
  const normalizedPath = path.startsWith('/') ? path : `/${path}`;

  // Employee, TeamLead, HigherManagement common routes:
  // /workspace, /, /employees, /profile, /planning
  if (
    normalizedPath === '/' ||
    normalizedPath === '/workspace' ||
    normalizedPath === '/employees' ||
    normalizedPath === '/profile' ||
    normalizedPath === '/planning'
  ) {
    return true;
  }

  // Review Queue requires reviews:manage (TeamLead, HigherManagement)
  if (normalizedPath === '/reviews') {
    if (permissions && permissions.includes('reviews:manage')) return true;
    return role === 'TeamLead' || role === 'HigherManagement';
  }

  // External Sync requires sync:trigger (TeamLead, HigherManagement)
  if (normalizedPath === '/sync') {
    if (permissions && permissions.includes('sync:trigger')) return true;
    return role === 'TeamLead' || role === 'HigherManagement';
  }

  // Management Portal requires management:* (HigherManagement only)
  if (normalizedPath === '/management') {
    if (permissions && permissions.some((p) => p.startsWith('management:'))) return true;
    return role === 'HigherManagement';
  }

  return true;
}
