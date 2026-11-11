import type { HttpErrorResponse } from '@angular/common/http';
import { describe, expect, it } from 'vitest';
import { apiErrorMessage, apiFieldErrors } from './api-error';
import { errorMessage, previewFromConflict } from './error.logic';

const envelope = (body: object) => ({ success: false, timestamp: '2026-01-01T00:00:00Z', ...body });
const httpError = (status: number, body: object) => ({ status, error: body, message: '' }) as unknown as HttpErrorResponse;

describe('erreurs au format enveloppe', () => {
  it('affiche le message métier', () => {
    const err = httpError(400, envelope({ message: 'La date de fin doit suivre la date de début.' }));
    expect(apiErrorMessage(err)).toBe('La date de fin doit suivre la date de début.');
    expect(errorMessage(err)).toBe('La date de fin doit suivre la date de début.');
  });

  it('rattache une erreur métier à son champ, comme message de confiance', () => {
    const err = httpError(400, envelope({ message: 'Le login est obligatoire.', field: 'login' }));
    const { fields, trusted } = apiFieldErrors(err);
    expect(fields['login']).toBe('Le login est obligatoire.');
    expect(trusted['login']).toBe(true);
  });

  it('préfère le détail des erreurs de validation au message générique', () => {
    const err = httpError(400, envelope({ message: 'Requête invalide.', errors: { '$.roleId': ['Valeur invalide.'] } }));
    expect(apiErrorMessage(err)).toBe('Valeur invalide.');
    expect(apiFieldErrors(err).fields['roleid']).toBe('Valeur invalide.');
  });

  it("lit l'aperçu d'un changement de rôle dans data sur un 409", () => {
    const preview = { adjustmentsToClear: ['users.read'], count: 1, accountLogin: 'jdoe' };
    const err = httpError(409, envelope({ message: 'Confirmation requise.', data: preview }));
    expect(previewFromConflict(err)).toEqual(preview);
  });
});
