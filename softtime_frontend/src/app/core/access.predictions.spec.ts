import { describe, expect, it } from 'vitest';
import { AccessContext, canAccess, visibleSections } from './access.logic';

const NAV = [
  {
    title: 'États',
    droit: 6,
    items: [
      { label: 'Rapports', droit: 6, permission: 'REPORTS:REPORTS' },
      { label: 'Prévisions', droit: 6, permission: 'REPORTS:PREDICTIONS' },
    ],
  },
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

describe('REPORTS:PREDICTIONS', () => {
  it('le menu Prévisions exige la permission ET la porte 6', () => {
    expect(labels(ctx({ permissions: ['REPORTS:PREDICTIONS'], gates: [6] }))).toEqual(['Prévisions']);
    expect(labels(ctx({ permissions: ['REPORTS:REPORTS', 'REPORTS:PREDICTIONS'], gates: [6] }))).toEqual(['Rapports', 'Prévisions']);
  });

  it('la permission sans la porte 6 ne donne pas accès', () => {
    expect(canAccess(ctx({ permissions: ['REPORTS:PREDICTIONS'], gates: [3] }), 'REPORTS:PREDICTIONS', 6)).toBe(false);
  });

  it('la porte 6 sans la permission fine ne montre que les Rapports', () => {
    expect(labels(ctx({ permissions: ['REPORTS:REPORTS'], gates: [6] }))).toEqual(['Rapports']);
  });

  it('un compte sans ajustement ni liste suit la porte 6 ; l’administrateur voit tout', () => {
    expect(labels(ctx({ gates: [6] }))).toEqual(['Rapports', 'Prévisions']);
    expect(labels(ctx({ roleId: 1 }))).toEqual(['Rapports', 'Prévisions']);
    expect(labels(ctx({ gates: [3] }))).toEqual([]);
  });
});
