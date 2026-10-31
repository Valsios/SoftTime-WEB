import { describe, expect, it } from 'vitest';
import {
  AccessContext,
  canAccess,
  decodeTokenGates,
  gateAllows,
  hasUsableDatabase,
  permissionAllows,
  visibleSections,
} from './access.logic';

const section = (title: string, droit: number, items: { label: string; droit: number; permission?: string }[]) => ({ title, droit, items });

const NAV = [
  section('Bases', 1, [{ label: 'Bases RH', droit: 1, permission: 'SAGE' }]),
  section('Paramètres', 2, [
    { label: 'Utilisateurs', droit: 2, permission: 'PARAMS:USERS' },
    { label: 'Catégories', droit: 2, permission: 'PARAMS:CATEGORIES' },
  ]),
  section('Traitement', 3, [
    { label: 'Shifts', droit: 3, permission: 'PROC:SHIFTS' },
    { label: 'Pointages', droit: 3, permission: 'PROC:PUNCHES' },
  ]),
  section('États', 6, [{ label: 'Rapports', droit: 6, permission: 'REPORTS:REPORTS' }]),
];

const ctx = (over: Partial<AccessContext>): AccessContext => ({
  roleId: 10034,
  permissions: [],
  customized: false,
  gates: [],
  fallbackRights: [],
  ...over,
});

const labels = (c: AccessContext) => visibleSections(NAV, c).flatMap((s) => s.items.map((i) => i.label));

/** Jeton factice : en-tête.charge.signature, la charge est en base64url. */
const fakeToken = (claims: Record<string, unknown>) =>
  `x.${btoa(JSON.stringify(claims)).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '')}.y`;

describe('access.logic', () => {
  it('administrateur : tout est visible, même sans porte ni permission', () => {
    const admin = ctx({ roleId: 1, gates: [], permissions: [] });
    expect(labels(admin)).toEqual(['Bases RH', 'Utilisateurs', 'Catégories', 'Shifts', 'Pointages', 'Rapports']);
    expect(canAccess(admin, 'PARAMS:USERS', 2)).toBe(true);
  });

  it('rôle seul : permission fine et porte présentes', () => {
    const role = ctx({ permissions: ['SAGE:BASE1:READ', 'PROC:SHIFTS', 'REPORTS:REPORTS'], customized: false, gates: [1, 3, 6] });
    expect(labels(role)).toEqual(['Bases RH', 'Shifts', 'Rapports']);
  });

  it('rôle plus ajouts : un ajout PARAMS avec la porte 2 rend l’écran visible', () => {
    const base = ['SAGE:BASE1:READ', 'PROC:SHIFTS'];
    expect(labels(ctx({ permissions: base, customized: true, gates: [1, 3] }))).toEqual(['Bases RH', 'Shifts']);
    const withAdds = ctx({ permissions: [...base, 'PARAMS:USERS'], customized: true, gates: [1, 2, 3] });
    expect(labels(withAdds)).toEqual(['Bases RH', 'Utilisateurs', 'Shifts']);
  });

  it('aucun droit : rien n’est visible et toutes les sections sont masquées', () => {
    const none = ctx({ permissions: [], customized: true, gates: [] });
    expect(labels(none)).toEqual([]);
    expect(visibleSections(NAV, none)).toEqual([]);
  });

  it('porte sans permission : l’écran reste masqué', () => {
    const c = ctx({ permissions: ['PROC:SHIFTS'], customized: true, gates: [3, 6] });
    expect(canAccess(c, 'PROC:PUNCHES', 3)).toBe(false);
    expect(canAccess(c, 'REPORTS:REPORTS', 6)).toBe(false);
    expect(labels(c)).toEqual(['Shifts']);
  });

  it('permission sans porte : l’écran est masqué (il serait refusé par le serveur)', () => {
    const c = ctx({ permissions: ['PARAMS:USERS', 'PROC:SHIFTS'], customized: true, gates: [3] });
    expect(permissionAllows(c, 'PARAMS:USERS', 2)).toBe(true);
    expect(gateAllows(c, 2)).toBe(false);
    expect(canAccess(c, 'PARAMS:USERS', 2)).toBe(false);
    expect(labels(c)).toEqual(['Shifts']);
  });

  it('une section dont tous les éléments sont masqués disparaît', () => {
    const c = ctx({ permissions: ['PARAMS:USERS'], customized: true, gates: [2] });
    const sections = visibleSections(NAV, c);
    expect(sections.map((s) => s.title)).toEqual(['Paramètres']);
    expect(sections[0].items.map((i) => i.label)).toEqual(['Utilisateurs']);
  });

  it('sans liste de permissions ni écart, la porte suffit (repli historique)', () => {
    const c = ctx({ permissions: [], customized: false, gates: [3] });
    expect(canAccess(c, 'PROC:SHIFTS', 3)).toBe(true);
    expect(canAccess(c, 'PARAMS:USERS', 2)).toBe(false);
  });

  it('jeton illisible : repli sur les portes renvoyées à la connexion', () => {
    const c = ctx({ permissions: ['PROC:SHIFTS'], customized: true, gates: null, fallbackRights: [3] });
    expect(canAccess(c, 'PROC:SHIFTS', 3)).toBe(true);
    expect(canAccess(ctx({ permissions: ['PROC:SHIFTS'], customized: true, gates: null, fallbackRights: [] }), 'PROC:SHIFTS', 3)).toBe(false);
  });

  it('sans porte demandée, seul le code de permission compte', () => {
    const c = ctx({ permissions: ['SAGE:BASE1:READ'], customized: true, gates: [] });
    expect(canAccess(c, 'SAGE:BASE1:READ')).toBe(true);
    expect(canAccess(c, 'SAGE:BASE2:READ')).toBe(false);
    expect(canAccess(c)).toBe(true);
  });

  it('décode les portes du claim rights du jeton', () => {
    expect(decodeTokenGates(fakeToken({ rights: '1,3,6' }))).toEqual([1, 3, 6]);
    expect(decodeTokenGates(fakeToken({ rights: '' }))).toEqual([]);
    expect(decodeTokenGates(fakeToken({ sub: '1' }))).toEqual([]);
    expect(decodeTokenGates(fakeToken({ rights: [2, 3] }))).toEqual([2, 3]);
  });

  it('jeton absent ou illisible : null', () => {
    expect(decodeTokenGates(null)).toBeNull();
    expect(decodeTokenGates('')).toBeNull();
    expect(decodeTokenGates('abc')).toBeNull();
    expect(decodeTokenGates('a.!!!.c')).toBeNull();
  });

  it('base utilisable : au moins une base avec un nom', () => {
    expect(hasUsableDatabase([{ nomBd: 'BASE1' }])).toBe(true);
    expect(hasUsableDatabase([{ nomBd: null }, { nomBd: '' }])).toBe(false);
    expect(hasUsableDatabase([])).toBe(false);
    expect(hasUsableDatabase(undefined)).toBe(false);
  });
});
