/** Relecture des droits à la navigation : au plus une fois toutes les 5 minutes (décision 5, section 11). */
export const NAVIGATION_REFRESH_MS = 5 * 60 * 1000;

/** Relecture après un refus (403) : immédiate, mais au plus une fois toutes les 10 secondes (une rafale de 403 = un appel). */
export const FORBIDDEN_REFRESH_MS = 10 * 1000;

/** Vrai si la dernière relecture date de plus de `intervalMs` (ou n'a jamais eu lieu). */
export function isRefreshDue(lastRefreshAt: number | null, now: number, intervalMs: number): boolean {
  return lastRefreshAt === null || now - lastRefreshAt >= intervalMs;
}
