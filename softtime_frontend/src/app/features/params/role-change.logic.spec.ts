import { describe, expect, it } from 'vitest';
import { parseRoleChangePreview, previewFromConflict } from '../../core/error.logic';
import { confirmFlag, confirmationMessage, needsConfirmation } from './role-change.logic';

const preview = (count: number, login = 'joel') => ({
  adjustmentsToClear: Array.from({ length: count }, (_, i) => `+PARAMS:X${i}`),
  count,
  accountLogin: login,
});

describe('role-change.logic', () => {
  it('une confirmation n’est demandée que si des ajustements seraient effacés', () => {
    expect(needsConfirmation(preview(37))).toBe(true);
    expect(needsConfirmation(preview(1))).toBe(true);
    expect(needsConfirmation(preview(0))).toBe(false);
    expect(needsConfirmation(null)).toBe(false);
    expect(needsConfirmation(undefined)).toBe(false);
  });

  it('le message nomme le compte et le nombre de droits effacés', () => {
    expect(confirmationMessage(preview(37, 'joel')))
      .toBe('Changer le rôle de joel effacera ses 37 droits personnalisés. Continuer ?');
  });

  it('le message est au singulier pour un seul droit', () => {
    expect(confirmationMessage(preview(1, 'User1')))
      .toBe('Changer le rôle de User1 effacera son droit personnalisé. Continuer ?');
  });

  it('confirmClearAdjustments n’est envoyé que si l’utilisateur a confirmé et que le rôle a changé', () => {
    expect(confirmFlag(true, 10034, 29)).toBe(true);
    expect(confirmFlag(true, 10034, '29' as unknown as number)).toBe(true);
    expect(confirmFlag(false, 10034, 29)).toBe(false);
    expect(confirmFlag(true, 10034, 10034)).toBe(false);   // rôle revenu à l’original : rien à effacer
    expect(confirmFlag(true, null, 29)).toBe(false);       // création
    expect(confirmFlag(true, 10034, null)).toBe(false);
  });

  it('un 409 contenant un aperçu est une demande de confirmation', () => {
    const body = { adjustmentsToClear: ['+shifts.read', '-punches.manage'], count: 2, accountLogin: 'joel' };
    expect(previewFromConflict({ status: 409, error: body })).toEqual(body);
  });

  it('un 409 sans aperçu, ou un autre statut, reste une erreur', () => {
    expect(previewFromConflict({ status: 409, error: { error: 'Conflit.' } })).toBeNull();
    expect(previewFromConflict({ status: 409, error: null })).toBeNull();
    expect(previewFromConflict({ status: 400, error: { adjustmentsToClear: [], count: 0, accountLogin: 'a' } })).toBeNull();
  });

  it('lit un aperçu même sans login et ignore les entrées invalides', () => {
    expect(parseRoleChangePreview({ adjustmentsToClear: ['+A', 3], count: 1 }))
      .toEqual({ adjustmentsToClear: ['+A'], count: 1, accountLogin: '' });
    expect(parseRoleChangePreview('texte')).toBeNull();
    expect(parseRoleChangePreview({ count: 1 })).toBeNull();
  });
});
