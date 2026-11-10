import { describe, expect, it } from 'vitest';
import {
  countBySource,
  legacyRemovalMessage,
  readablePrivilegesError,
  selfRightsRemovalMessage,
} from './privileges.logic';

/** Refonte des privilèges (phase finale) : messages lisibles, sources des droits, anciens codes, protections. */
describe('privileges.logic — refonte', () => {
  const http = (message: string) => ({ status: 400, error: { success: false, message } });

  it('readablePrivilegesError : code inconnu reformulé avec la marche à suivre', () => {
    const text = readablePrivilegesError(http('Codes de permission inconnus : INVALID:HACK.'));
    expect(text).toBe('Droits non reconnus par le serveur (INVALID:HACK). Rechargez la page : la liste des droits a peut-être changé.');
  });

  it('readablePrivilegesError : code trop long reformulé sans détail technique', () => {
    const text = readablePrivilegesError(http('Code de permission trop long (128 caractères maximum) : SAGE:XXXX:READ.'));
    expect(text).toContain('128 caractères au plus');
    expect(text).not.toContain('SAGE:XXXX');
  });

  it('readablePrivilegesError : action réservée aux administrateurs', () => {
    const text = readablePrivilegesError({ status: 403, error: { message: 'Cette opération est réservée aux administrateurs.' } });
    expect(text).toBe('Seul un administrateur peut modifier les privilèges.');
  });

  it('readablePrivilegesError : refus de porte affiché tel quel', () => {
    expect(readablePrivilegesError({ status: 403, error: { message: "Vous n'avez pas accès à cette section." } })).toBe(
      "Vous n'avez pas accès à cette section.",
    );
  });

  it('countBySource : hérités et ajoutés effectifs, retirés', () => {
    const counts = countBySource([
      { source: 'ROLE', isEffective: true },
      { source: 'ROLE', isEffective: true },
      { source: 'ADDED', isEffective: true },
      { source: 'ADDED', isEffective: false },
      { source: 'REMOVED', isEffective: false },
    ]);
    expect(counts).toEqual({ role: 2, added: 1, removed: 1 });
  });

  it('selfRightsRemovalMessage : annonce le nombre de droits retirés à soi-même', () => {
    expect(selfRightsRemovalMessage(3)).toContain('retirer 3 droit(s) à votre propre compte');
  });

  it('legacyRemovalMessage : nomme le code et l’effet sur la porte Bases', () => {
    const text = legacyRemovalMessage('SAGE:ANCIENNE');
    expect(text).toContain('« SAGE:ANCIENNE »');
    expect(text).toContain('porte Bases');
  });
});
