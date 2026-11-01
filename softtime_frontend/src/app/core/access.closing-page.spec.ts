import { describe, expect, it } from 'vitest';
import { NAV_SECTIONS } from '../layout/nav';
import { AccessContext, canAccess, visibleSections } from './access.logic';

// Menu « Clôture de paie » et garde de la route /closing : même règle que le serveur (porte 6 ET REPORTS:CLOSING).
const ctx = (over: Partial<AccessContext>): AccessContext => ({
  roleId: 10034,
  permissions: [],
  customized: false,
  gates: [],
  fallbackRights: [],
  ...over,
});

const visible = (c: AccessContext) =>
  visibleSections(NAV_SECTIONS, c)
    .flatMap((s) => s.items)
    .some((i) => i.path === '/closing');

// Données de route de /closing (app.routes.ts) : le garde droitGuard appelle canAccess(permission, droit).
const ROUTE = { permission: 'REPORTS:CLOSING', droit: 6 };
const guardAllows = (c: AccessContext) => canAccess(c, ROUTE.permission, ROUTE.droit);

describe('menu et garde « Clôture de paie »', () => {
  it('l’élément de menu est dans la section États, porte 6 et REPORTS:CLOSING', () => {
    const etats = NAV_SECTIONS.find((s) => s.title === 'États');
    expect(etats?.items.find((i) => i.path === '/closing')).toMatchObject({
      label: 'Clôture de paie',
      droit: 6,
      permission: 'REPORTS:CLOSING',
    });
  });

  it('porte 6 + permission : menu visible et garde ouverte', () => {
    const c = ctx({ permissions: ['REPORTS:CLOSING'], gates: [6] });
    expect(visible(c)).toBe(true);
    expect(guardAllows(c)).toBe(true);
  });

  it('permission sans la porte 6 : menu masqué et garde fermée', () => {
    const c = ctx({ permissions: ['REPORTS:CLOSING'], gates: [1, 3] });
    expect(visible(c)).toBe(false);
    expect(guardAllows(c)).toBe(false);
  });

  it('porte 6 sans la permission (autres droits États) : menu masqué et garde fermée', () => {
    const c = ctx({ permissions: ['REPORTS:REPORTS', 'REPORTS:PREDICTIONS'], gates: [6] });
    expect(visible(c)).toBe(false);
    expect(guardAllows(c)).toBe(false);
  });

  it('administrateur : visible ; compte sans liste suit la porte 6', () => {
    expect(visible(ctx({ roleId: 1 }))).toBe(true);
    expect(guardAllows(ctx({ roleId: 1 }))).toBe(true);
    expect(visible(ctx({ gates: [6] }))).toBe(true);
    expect(visible(ctx({ gates: [3] }))).toBe(false);
  });
});
