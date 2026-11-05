import { describe, expect, it } from 'vitest';
import {
  applyToggle,
  computeWarnings,
  filterCategories,
  isSensitiveRight,
  matchesAnySearch,
  normalizeSearch,
  sensitiveGranted,
} from './privileges.advice';

const pages = new Set(['PARAMS:USERS', 'PROC:SHIFTS']);

describe('computeWarnings', () => {
  it('avertit si un droit de travail est accordé sans lecture de base (utilisateur)', () => {
    const w = computeWarnings(['PROC:SHIFTS'], 'user');
    expect(w).toHaveLength(1);
    expect(w[0]).toContain('ce compte ne verra aucune donnée dans Traitements, Rapports ou Autres');
    expect(w[0]).toContain('Accès bases');
  });

  it('adapte le texte au mode rôle', () => {
    expect(computeWarnings(['REPORTS:REPORTS'], 'role')[0]).toContain('les personnes de ce rôle ne verront aucune donnée');
    expect(computeWarnings(['OTHER:CANTEEN'], 'role')).toHaveLength(1);
  });

  it('ne dit rien avec une lecture de base, ou sans droit de travail', () => {
    expect(computeWarnings(['PROC:SHIFTS', 'SAGE:BASE1:READ'], 'user')).toEqual([]);
    expect(computeWarnings(['PARAMS:CARDPAIE', 'SAGE:BASE1:UPDATE'], 'user')).toEqual([]);
    expect(computeWarnings([], 'role')).toEqual([]);
  });

  it('une base en écriture seule ne compte pas comme accès', () => {
    expect(computeWarnings(['PROC:SHIFTS', 'SAGE:BASE1:UPDATE'], 'user')).toHaveLength(1);
    expect(computeWarnings(['PROC:SHIFTS', 'POINTEUSE:PTG:READ'], 'user')).toHaveLength(1);
  });

  it('signale les droits sensibles accordés, avec leurs libellés', () => {
    const w = computeWarnings(['PARAMS:USERS', 'PARAMS:ROLES', 'SAGE:B:READ'], 'user');
    expect(w).toEqual(['Droit sensible accordé : Utilisateurs, Rôles.']);
  });

  it('ne signale pas un droit sensible déjà enregistré', () => {
    expect(computeWarnings(['PARAMS:USERS'], 'user', ['PARAMS:USERS'])).toEqual([]);
    expect(sensitiveGranted(['PARAMS:DB_ACCESS', 'PARAMS:PRIVILEGES'], ['PARAMS:PRIVILEGES'])).toEqual(['Accès bases']);
  });

  it('cumule les deux avertissements', () => {
    expect(computeWarnings(['PROC:SHIFTS', 'PARAMS:PRIVILEGES'], 'role')).toHaveLength(2);
  });

  it('reconnaît les quatre droits sensibles', () => {
    for (const c of ['PARAMS:USERS', 'PARAMS:ROLES', 'PARAMS:PRIVILEGES', 'PARAMS:DB_ACCESS']) expect(isSensitiveRight(c)).toBe(true);
    expect(isSensitiveRight('PARAMS:CARDPAIE')).toBe(false);
    expect(isSensitiveRight('PARAMS:USERS:CREATE')).toBe(false);
  });
});

describe('applyToggle (dépendances)', () => {
  it('cocher une action coche aussi sa page', () => {
    const next = applyToggle(new Set(), 'PROC:SHIFTS:UPDATE', true, pages);
    expect([...next].sort()).toEqual(['PROC:SHIFTS', 'PROC:SHIFTS:UPDATE']);
  });

  it('décocher une page décoche ses actions, pas celles des autres pages', () => {
    const start = new Set(['PROC:SHIFTS', 'PROC:SHIFTS:CREATE', 'PROC:SHIFTS:DELETE', 'PARAMS:USERS', 'PARAMS:USERS:CREATE']);
    const next = applyToggle(start, 'PROC:SHIFTS', false, pages);
    expect([...next].sort()).toEqual(['PARAMS:USERS', 'PARAMS:USERS:CREATE']);
  });

  it('décocher une action laisse la page cochée', () => {
    const next = applyToggle(new Set(['PROC:SHIFTS', 'PROC:SHIFTS:CREATE']), 'PROC:SHIFTS:CREATE', false, pages);
    expect([...next]).toEqual(['PROC:SHIFTS']);
  });

  it('cocher une page ne coche pas ses actions', () => {
    expect([...applyToggle(new Set(), 'PROC:SHIFTS', true, pages)]).toEqual(['PROC:SHIFTS']);
  });

  it('les codes hors pages (bases) ne déclenchent aucune dépendance', () => {
    expect([...applyToggle(new Set(), 'SAGE:CREATE', true, pages)]).toEqual(['SAGE:CREATE']);
    expect([...applyToggle(new Set(['SAGE:B:UPDATE']), 'SAGE:B:UPDATE', false, pages)]).toEqual([]);
  });

  it('conserve les actions obligatoires et ne modifie pas l’ensemble reçu', () => {
    const start = new Set(['PARAMS:USERS', 'PARAMS:USERS:CREATE', 'PARAMS:USERS:UPDATE']);
    const next = applyToggle(start, 'PARAMS:USERS', false, pages, (c) => c === 'PARAMS:USERS:CREATE');
    expect([...next]).toEqual(['PARAMS:USERS:CREATE']);
    expect(start.size).toBe(3);
  });
});

describe('recherche', () => {
  const cats = [
    { key: 'A', title: 'Paramètres', items: [{ label: 'Utilisateurs' }, { label: 'Tolérances' }, { label: 'Jours fériés' }] },
    { key: 'B', title: 'Traitements', items: [{ label: 'Shifts' }, { label: 'Heures supplémentaires' }] },
  ];

  it('ignore la casse et les accents', () => {
    expect(normalizeSearch('  FÉRIÉS ')).toBe('feries');
    expect(filterCategories(cats, 'feries')[0].items.map((i) => i.label)).toEqual(['Jours fériés']);
    expect(filterCategories(cats, 'TOLERANCE')[0].items.map((i) => i.label)).toEqual(['Tolérances']);
  });

  it('masque les catégories sans résultat', () => {
    expect(filterCategories(cats, 'shift').map((c) => c.key)).toEqual(['B']);
    expect(filterCategories(cats, 'zzz')).toEqual([]);
  });

  it('une recherche vide rend tout, sans modifier les catégories', () => {
    expect(filterCategories(cats, '   ')).toHaveLength(2);
    filterCategories(cats, 'shift');
    expect(cats[0].items).toHaveLength(3);
    expect(cats[1].items).toHaveLength(2);
  });

  it('retrouve une section par une de ses étiquettes', () => {
    expect(matchesAnySearch(['Bases RH / Paie', 'BASE1'], 'paie')).toBe(true);
    expect(matchesAnySearch(['Bases de pointage', 'PTG'], 'paie')).toBe(false);
    expect(matchesAnySearch([], '')).toBe(true);
  });
});
