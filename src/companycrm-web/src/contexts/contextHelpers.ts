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
