/** Returns today as YYYY-MM-DD (local). */
export function today(): string {
  return toIsoDate(new Date());
}

function toIsoDate(d: Date): string {
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

export function parseLocalDate(iso: string): Date {
  const [y, m, d] = iso.split('-').map(Number);
  return new Date(y, (m ?? 1) - 1, d ?? 1);
}

/** Monday of the current week (local). */
export function currentMonday(): string {
  const d = new Date();
  const day = d.getDay();
  d.setDate(d.getDate() + (day === 0 ? -6 : 1 - day));
  return toIsoDate(d);
}

/** Sunday of the current week (local). */
export function currentSunday(): string {
  const d = parseLocalDate(currentMonday());
  d.setDate(d.getDate() + 6);
  return toIsoDate(d);
}

export function isMondayToSundayWeek(from: string, to: string): boolean {
  const a = parseLocalDate(from);
  const b = parseLocalDate(to);
  const days = (b.getTime() - a.getTime()) / 86_400_000;
  return a.getDay() === 1 && b.getDay() === 0 && days === 6;
}

/** First day of current month. */
export function monthStart(): string {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-01`;
}

/** Formats an ISO date/time string for display. */
export function fmtDate(value: unknown): string {
  if (!value) return '';
  const s = String(value);
  return s.length >= 10 ? s.slice(0, 10) : s;
}

/** Maps an object to a table row (keys preserved). */
export function asRow<T extends object>(item: T): Record<string, unknown> {
  return { ...(item as Record<string, unknown>) };
}
