import { describe, expect, it } from 'vitest';
import { describeSelfError, serverMessage, strongestNotice } from './self-space.errors';

describe('Mon espace : classement des erreurs', () => {
  it('serverMessage : champ error ou message d\'un corps JSON, jamais un JSON, du HTML ou un texte trop long', () => {
    expect(serverMessage({ error: { error: ' Ce compte n\'a pas accès. ' } })).toBe('Ce compte n\'a pas accès.');
    expect(serverMessage({ error: { message: 'Texte' } })).toBe('Texte');
    expect(serverMessage({ error: '{"error":"x"}' })).toBeNull();
    expect(serverMessage({ error: '<html></html>' })).toBeNull();
    expect(serverMessage({ error: 'x'.repeat(301) })).toBeNull();
    expect(serverMessage({ error: { title: 'Bad Request' } })).toBeNull();
    expect(serverMessage(null)).toBeNull();
    expect(serverMessage({})).toBeNull();
  });

  it('statut -> type, consigne et masquage des cartes', () => {
    const type = (e: unknown) => {
      const n = describeSelfError(e);
      return [n.kind, n.hint, n.masksCards];
    };

    expect(type({ status: 403, error: { error: 'Ce matricule est inconnu dans la base : contactez la RH.' } })).toEqual(['account', 'Contactez la RH.', true]);
    expect(type({ status: 403, error: { error: 'Le matricule de ce compte a changé depuis la connexion : reconnectez-vous.' } })).toEqual(['reconnect', 'Reconnectez-vous.', true]);
    expect(type({ status: 401 })).toEqual(['reconnect', 'Reconnectez-vous.', true]);
    expect(type({ status: 400, error: { error: 'Période invalide.' } })).toEqual(['period', 'Choisissez un autre mois.', true]);
    expect(type({ status: 500 })).toEqual(['server', 'Réessayez dans un instant.', false]);
    expect(type({ status: 503 })).toEqual(['server', 'Réessayez dans un instant.', false]);
    expect(type({ status: 0 })).toEqual(['network', 'Réessayez dans un instant.', false]);
    expect(type(new Error('x'))).toEqual(['network', 'Réessayez dans un instant.', false]);
  });

  it('strongestNotice : reconnexion > compte > période > serveur > réseau, et le premier gagne à égalité', () => {
    const n = (status: number, message = 'm') => describeSelfError({ status, error: { error: message } });
    const reco = n(403, 'reconnectez-vous');
    const compte = n(403);
    const periode = n(400);
    const serveur = n(500);
    const reseau = n(0);

    expect(strongestNotice(null, reseau)).toBe(reseau);
    expect(strongestNotice(reseau, serveur)).toBe(serveur);
    expect(strongestNotice(serveur, periode)).toBe(periode);
    expect(strongestNotice(periode, compte)).toBe(compte);
    expect(strongestNotice(compte, reco)).toBe(reco);
    expect(strongestNotice(reco, reseau)).toBe(reco);
    expect(strongestNotice(compte, n(403, 'autre'))).toBe(compte);
  });
});
