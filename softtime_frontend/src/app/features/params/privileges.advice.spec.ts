import { describe, expect, it } from 'vitest';
import {
  actionCode,
  applyToggle,
  computeWarnings,
  filterCategories,
  isSensitiveRight,
  matchesAnySearch,
  normalizeSearch,
  sensitiveGranted,
} from './privileges.advice';

const pages = new Set(['users.read', 'shifts.read']);
/** Droits de travail (portes 3, 4, 6) tels que la page les tire du catalogue. */
const WORK: ReadonlySet<string> = new Set(['shifts.read', 'reports.read', 'canteen.manage']);

describe('computeWarnings', () => {
  it('avertit si un droit de travail est accordé sans lecture de base (utilisateur)', () => {
    const w = computeWarnings(['shifts.read'], 'user', [], WORK);
    expect(w).toHaveLength(1);
    expect(w[0]).toContain('ce compte ne verra aucune donnée dans Traitements, Rapports ou Autres');
    expect(w[0]).toContain('Accès bases');
  });

  it('adapte le texte au mode rôle', () => {
    expect(computeWarnings(['reports.read'], 'role', [], WORK)[0]).toContain('les personnes de ce rôle ne verront aucune donnée');
    expect(computeWarnings(['canteen.manage'], 'role', [], WORK)).toHaveLength(1);
  });

  it('ne dit rien avec une lecture de base, ou sans droit de travail', () => {
    expect(computeWarnings(['shifts.read', 'sage.BASE1.read'], 'user', [], WORK)).toEqual([]);
    expect(computeWarnings(['card-paie.read', 'sage.BASE1.update'], 'user', [], WORK)).toEqual([]);
    expect(computeWarnings([], 'role', [], WORK)).toEqual([]);
  });

  it('une base en écriture seule ne compte pas comme accès', () => {
    expect(computeWarnings(['shifts.read', 'sage.BASE1.update'], 'user', [], WORK)).toHaveLength(1);
    expect(computeWarnings(['shifts.read', 'pointeuse.PTG.read'], 'user', [], WORK)).toHaveLength(1);
  });

  it('signale les droits sensibles accordés, avec leurs libellés', () => {
    const w = computeWarnings(['users.read', 'roles.read', 'sage.B.read'], 'user', [], WORK);
    expect(w).toEqual(['Droit sensible accordé : Utilisateurs, Rôles.']);
  });

  it('ne signale pas un droit sensible déjà enregistré', () => {
    expect(computeWarnings(['users.read'], 'user', ['users.read'], WORK)).toEqual([]);
    expect(sensitiveGranted(['db-access.read', 'privileges.read'], ['privileges.read'])).toEqual(['Accès bases']);
  });

  it('cumule les deux avertissements', () => {
    expect(computeWarnings(['shifts.read', 'privileges.read'], 'role', [], WORK)).toHaveLength(2);
  });

  it('reconnaît les quatre droits sensibles', () => {
    for (const c of ['users.read', 'roles.read', 'privileges.read', 'db-access.read']) expect(isSensitiveRight(c)).toBe(true);
    expect(isSensitiveRight('card-paie.read')).toBe(false);
    expect(isSensitiveRight('users.create')).toBe(false);
  });
});

describe('applyToggle (dépendances)', () => {
  it('cocher une action coche aussi sa page', () => {
    const next = applyToggle(new Set(), 'shifts.update', true, pages);
    expect([...next].sort()).toEqual(['shifts.read', 'shifts.update']);
  });

  it('décocher une page décoche ses actions, pas celles des autres pages', () => {
    const start = new Set(['shifts.read', 'shifts.create', 'shifts.delete', 'users.read', 'users.create']);
    const next = applyToggle(start, 'shifts.read', false, pages);
    expect([...next].sort()).toEqual(['users.create', 'users.read']);
  });

  it('décocher une action laisse la page cochée', () => {
    const next = applyToggle(new Set(['shifts.read', 'shifts.create']), 'shifts.create', false, pages);
    expect([...next]).toEqual(['shifts.read']);
  });

  it('cocher une page ne coche pas ses actions', () => {
    expect([...applyToggle(new Set(), 'shifts.read', true, pages)]).toEqual(['shifts.read']);
  });

  it('les codes hors pages (bases) ne déclenchent aucune dépendance', () => {
    expect([...applyToggle(new Set(), 'sage.create', true, pages)]).toEqual(['sage.create']);
    expect([...applyToggle(new Set(['sage.B.update']), 'sage.B.update', false, pages)]).toEqual([]);
  });

  it('conserve les actions obligatoires et ne modifie pas l’ensemble reçu', () => {
    const start = new Set(['users.read', 'users.create', 'users.update']);
    const next = applyToggle(start, 'users.read', false, pages, (c) => c === 'users.create');
    expect([...next]).toEqual(['users.create']);
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

describe('actionCode', () => {
  it("remplace l'action de la page par celle demandée", () => {
    expect(actionCode('categories.read', 'delete')).toBe('categories.delete');
    expect(actionCode('employee-planning.read', 'create')).toBe('employee-planning.create');
  });

  it("cocher une action d'un écran « manage » coche sa page", () => {
    const next = applyToggle(new Set<string>(), 'anomalies.delete', true, new Set(['anomalies.manage']));
    expect([...next].sort()).toEqual(['anomalies.delete', 'anomalies.manage']);
  });
});
