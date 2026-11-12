import { describe, expect, it } from 'vitest';
import {
  appliesAtNextLoginNote,
  buildResetToRoleMessage,
  computeSources,
  customizedBadgeLabel,
} from './privileges.logic';

const sorted = (s: Set<string>) => [...s].sort();

describe('computeSources', () => {
  it('rôle seul : aucun écart', () => {
    const s = computeSources(['A', 'B'], ['A', 'B']);
    expect(s.count).toBe(0);
    expect(sorted(s.added)).toEqual([]);
    expect(sorted(s.removed)).toEqual([]);
  });

  it('ajouts : codes du compte absents du rôle', () => {
    const s = computeSources(['A'], ['A', 'B', 'C']);
    expect(sorted(s.added)).toEqual(['B', 'C']);
    expect(s.removed.size).toBe(0);
    expect(s.count).toBe(2);
  });

  it('retraits : codes du rôle absents du compte', () => {
    const s = computeSources(['A', 'B', 'C'], ['A']);
    expect(sorted(s.removed)).toEqual(['B', 'C']);
    expect(s.added.size).toBe(0);
    expect(s.count).toBe(2);
  });

  it('ajouts et retraits ensemble', () => {
    const s = computeSources(['A', 'B'], ['B', 'C']);
    expect(sorted(s.added)).toEqual(['C']);
    expect(sorted(s.removed)).toEqual(['A']);
    expect(s.count).toBe(2);
    expect(customizedBadgeLabel(s)).toBe('Personnalisé · +1 \u22121');
  });

  it('marqueur « aucun droit » : ignoré, tous les droits du rôle sont retirés', () => {
    const s = computeSources(['A', 'B'], ['__SOFTTIME_EMPTY_CUSTOM_PERMISSIONS__']);
    expect(sorted(s.removed)).toEqual(['A', 'B']);
    expect(s.added.size).toBe(0);
    // Les marqueurs du rôle ne sont pas des droits non plus.
    expect(computeSources(['__SOFTTIME_EMPTY_ROLE_PERMISSIONS__'], []).count).toBe(0);
  });

  it('listes vides', () => {
    expect(computeSources([], []).count).toBe(0);
    expect(sorted(computeSources([], ['A']).added)).toEqual(['A']);
    expect(sorted(computeSources(['A'], []).removed)).toEqual(['A']);
  });

  it('accepte des ensembles et des tableaux', () => {
    expect(computeSources(new Set(['A']), new Set(['A', 'B'])).count).toBe(1);
  });
});

describe('messages de la page Privilèges', () => {
  it('confirmation de retour aux droits du rôle', () => {
    expect(buildResetToRoleMessage('Consultation', 37, false))
      .toBe('Revenir aux droits du rôle Consultation ? Les 37 ajustements personnels seront effacés.');
    expect(buildResetToRoleMessage('Consultation', 1, false))
      .toBe('Revenir aux droits du rôle Consultation ? Le 1 ajustement personnel sera effacé.');
  });

  it('la confirmation mentionne les modifications non enregistrées', () => {
    expect(buildResetToRoleMessage('Consultation', 2, true)).toContain('modifications non enregistrées');
    expect(buildResetToRoleMessage('Consultation', 2, false)).not.toContain('modifications non enregistrées');
  });

  it('message d’application dès la prochaine action', () => {
    expect(appliesAtNextLoginNote('user')).toBe("Les changements s'appliquent dès la prochaine action de la personne.");
    expect(appliesAtNextLoginNote('role')).toBe("Les changements s'appliquent dès la prochaine action des personnes de ce rôle.");
  });
});
