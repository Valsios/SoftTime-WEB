import { describe, expect, it } from 'vitest';
import {
  BASE_ACCESS_DENIED_MESSAGE,
  FORBIDDEN_DEFAULT_MESSAGE,
  bodyMessage,
  errorMessage,
  isBaseAccessDenied,
  isMissingBaseError,
} from './error.logic';

const NATIVE_403 = 'Http failure response for http://localhost/api/shifts: 403 Forbidden';

describe('error.logic', () => {
  it('un 403 sans corps affiche le message d’accès refusé, pas le texte natif d’Angular', () => {
    expect(errorMessage({ status: 403, error: null, message: NATIVE_403 })).toBe(FORBIDDEN_DEFAULT_MESSAGE);
    expect(errorMessage({ status: 403, error: '', message: NATIVE_403 })).toBe(FORBIDDEN_DEFAULT_MESSAGE);
    expect(errorMessage({ status: 403, error: {}, message: NATIVE_403 })).toBe(FORBIDDEN_DEFAULT_MESSAGE);
    expect(FORBIDDEN_DEFAULT_MESSAGE).toBe("Accès refusé : votre compte n'a pas le droit requis pour cette fonction.");
  });

  it('les messages applicatifs sont affichés tels quels', () => {
    expect(errorMessage({ status: 403, error: { error: BASE_ACCESS_DENIED_MESSAGE } })).toBe("Vous n'avez pas accès à cette base.");
    expect(errorMessage({ status: 403, error: { error: 'Cette opération est réservée aux administrateurs.' } }))
      .toBe('Cette opération est réservée aux administrateurs.');
    expect(errorMessage({ status: 400, error: { error: 'Cette base (serveur + nom) est déjà enregistrée.' } }))
      .toBe('Cette base (serveur + nom) est déjà enregistrée.');
  });

  it('lit aussi message, title et un corps texte', () => {
    expect(bodyMessage({ status: 400, error: { message: 'm' } })).toBe('m');
    expect(bodyMessage({ status: 400, error: { title: 't' } })).toBe('t');
    expect(bodyMessage({ status: 403, error: 'Refusé' })).toBe('Refusé');
    expect(bodyMessage({ status: 500, error: null })).toBe('');
  });

  it('autres statuts sans corps : texte natif, puis message par défaut', () => {
    expect(errorMessage({ status: 500, error: null, message: 'Http failure 500' })).toBe('Http failure 500');
    expect(errorMessage({ status: 500, error: null, message: null })).toBe('Une erreur est survenue.');
  });

  it('le 400 « En-tête X-Sage-Database requis. » est reconnu (remplacé par l’état vide)', () => {
    expect(isMissingBaseError({ status: 400, error: { error: 'En-tête X-Sage-Database requis.' } })).toBe(true);
    expect(isMissingBaseError({ status: 400, error: { error: 'Autre erreur.' } })).toBe(false);
    expect(isMissingBaseError({ status: 403, error: { error: 'En-tête X-Sage-Database requis.' } })).toBe(false);
  });

  it('le 403 « Vous n’avez pas accès à cette base » est reconnu, pas les autres 403', () => {
    expect(isBaseAccessDenied({ status: 403, error: { error: BASE_ACCESS_DENIED_MESSAGE } })).toBe(true);
    expect(isBaseAccessDenied({ status: 403, error: null })).toBe(false);
    expect(isBaseAccessDenied({ status: 403, error: { error: 'Cette opération est réservée aux administrateurs.' } })).toBe(false);
    expect(isBaseAccessDenied({ status: 401, error: { error: BASE_ACCESS_DENIED_MESSAGE } })).toBe(false);
  });
});
