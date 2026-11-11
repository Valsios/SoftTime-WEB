import { describe, expect, it } from 'vitest';
import {
  dayText,
  homeRoute,
  hasSelfPermission,
  hoursText,
  isSelfServiceProfile,
  minutesText,
  monthOptions,
  timeText,
  WINDOW_MONTHS,
} from './self-space.logic';

describe('Mon espace : profil et page d’arrivée', () => {
  it('hasSelfPermission : seulement la permission self.read exacte', () => {
    expect(hasSelfPermission(['self.read'])).toBe(true);
    expect(hasSelfPermission(['anomalies.manage', 'self.read'])).toBe(true);
    expect(hasSelfPermission(['SELF:READ:EXPORT', 'reports.read'])).toBe(false);
    expect(hasSelfPermission([])).toBe(false);
    expect(hasSelfPermission(null)).toBe(false);
    expect(hasSelfPermission(undefined)).toBe(false);
  });

  it('isSelfServiceProfile : self.read ET aucune porte', () => {
    expect(isSelfServiceProfile(['self.read'], [])).toBe(true);
    expect(isSelfServiceProfile(['self.read'], null)).toBe(true);
    expect(isSelfServiceProfile(['self.read'], [3])).toBe(false);        // une porte de gestion : comportement habituel
    expect(isSelfServiceProfile(['shifts.read'], [])).toBe(false);
    expect(isSelfServiceProfile([], [])).toBe(false);
  });

  it('homeRoute : Mon espace pour le seul profil Salarié, tableau de bord pour tout le reste (comportement inchangé)', () => {
    expect(homeRoute(['self.read'], [])).toBe('/mon-espace');
    expect(homeRoute([], [1, 2, 3, 4, 5, 6])).toBe('/dashboard');                  // administrateur
    expect(homeRoute(['shifts.read', 'punches.manage'], [3])).toBe('/dashboard');     // compte de gestion
    expect(homeRoute([], [])).toBe('/dashboard');                                   // compte sans permission détaillée
    expect(homeRoute(['self.read'], [6])).toBe('/dashboard');                       // permission + porte : comportement habituel
    expect(homeRoute(undefined, undefined)).toBe('/dashboard');
  });
});

describe('Mon espace : mois et formats', () => {
  it('monthOptions : les 4 derniers mois, le mois en cours en premier, jamais de jour futur', () => {
    const now = new Date(2026, 10, 6);                                              // 6 novembre 2026
    const mois = monthOptions(now);

    expect(mois).toHaveLength(WINDOW_MONTHS);
    expect(mois.map((m) => m.value)).toEqual(['2026-11', '2026-10', '2026-09', '2026-08']);
    expect(mois[0]).toMatchObject({ from: '2026-11-01', to: '2026-11-06' });        // mois en cours : jusqu'à aujourd'hui
    expect(mois[1]).toMatchObject({ from: '2026-10-01', to: '2026-10-31' });
    expect(mois[3]).toMatchObject({ from: '2026-08-01', to: '2026-08-31' });
    expect(mois[0].label).toBe('Novembre 2026');
  });

  it('monthOptions : passage d’année et février', () => {
    const mois = monthOptions(new Date(2026, 0, 15));
    expect(mois.map((m) => m.value)).toEqual(['2026-01', '2025-12', '2025-11', '2025-10']);
    expect(monthOptions(new Date(2026, 2, 3))[1]).toMatchObject({ from: '2026-02-01', to: '2026-02-28' });
  });

  it('formats : heures, jours, heures du jour, minutes', () => {
    expect(hoursText(7.5)).toBe('7,5 h');
    expect(hoursText(10)).toBe('10 h');
    expect(hoursText(null)).toBe('0 h');
    expect(dayText('2026-11-03T00:00:00')).toBe('03/11/2026');
    expect(dayText(null)).toBe('—');
    expect(timeText('08:20:00')).toBe('08:20');
    expect(timeText(null)).toBe('—');
    expect(minutesText(20)).toBe('20 min');
  });
});
