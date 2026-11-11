import { describe, expect, it } from 'vitest';
import { AccessContext, canAccess, visibleSections } from './access.logic';

// Droit de la clôture de paie assistée : même mécanique que predictions.read (porte 6 ET permission fine).
const NAV = [
  {
    title: 'États',
    droit: 6,
    items: [
      { label: 'Rapports', droit: 6, permission: 'reports.read' },
      { label: 'Prévisions', droit: 6, permission: 'predictions.read' },
      { label: 'Contrôle avant paie', droit: 6, permission: 'closing.read' },
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

describe('closing.read', () => {
  it('le Contrôle avant paie exige la permission ET la porte 6', () => {
    expect(labels(ctx({ permissions: ['closing.read'], gates: [6] }))).toEqual(['Contrôle avant paie']);
    expect(canAccess(ctx({ permissions: ['closing.read'], gates: [6] }), 'closing.read', 6)).toBe(true);
  });

  it('la permission sans la porte 6 ne donne pas accès', () => {
    expect(canAccess(ctx({ permissions: ['closing.read'], gates: [3] }), 'closing.read', 6)).toBe(false);
    expect(labels(ctx({ permissions: ['closing.read'], gates: [1, 3] }))).toEqual([]);
  });

  it('la porte 6 avec les autres droits États seulement ne montre pas le Contrôle avant paie', () => {
    expect(labels(ctx({ permissions: ['reports.read', 'predictions.read'], gates: [6] }))).toEqual(['Rapports', 'Prévisions']);
  });

  it('un compte sans ajustement ni liste suit la porte 6 ; l’administrateur voit tout', () => {
    expect(labels(ctx({ gates: [6] }))).toEqual(['Rapports', 'Prévisions', 'Contrôle avant paie']);
    expect(labels(ctx({ roleId: 1 }))).toEqual(['Rapports', 'Prévisions', 'Contrôle avant paie']);
    expect(labels(ctx({ gates: [3] }))).toEqual([]);
  });
});
