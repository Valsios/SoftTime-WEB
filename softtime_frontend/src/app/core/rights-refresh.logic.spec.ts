import { describe, expect, it } from 'vitest';
import { FORBIDDEN_REFRESH_MS, NAVIGATION_REFRESH_MS, isRefreshDue } from './rights-refresh.logic';

/** Décision 5 : droits relus après un 403 (au plus toutes les 10 s) et à la navigation (au plus toutes les 5 min). */
describe('rights-refresh.logic', () => {
  it('jamais relus : relecture due', () => {
    expect(isRefreshDue(null, 1_000, NAVIGATION_REFRESH_MS)).toBe(true);
  });

  it('navigation : pas avant 5 minutes, puis due', () => {
    expect(isRefreshDue(0, NAVIGATION_REFRESH_MS - 1, NAVIGATION_REFRESH_MS)).toBe(false);
    expect(isRefreshDue(0, NAVIGATION_REFRESH_MS, NAVIGATION_REFRESH_MS)).toBe(true);
  });

  it('403 : une rafale de refus ne déclenche qu’une relecture toutes les 10 secondes', () => {
    expect(isRefreshDue(0, 2_000, FORBIDDEN_REFRESH_MS)).toBe(false);
    expect(isRefreshDue(0, FORBIDDEN_REFRESH_MS, FORBIDDEN_REFRESH_MS)).toBe(true);
  });
});
