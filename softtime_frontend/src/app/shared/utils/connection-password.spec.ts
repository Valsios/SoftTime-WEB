import { describe, expect, it } from 'vitest';
import {
  connectionPayload,
  connectionTargetChanged,
  discoveryAuth,
  stripDatabasePasswords,
  usesStoredPassword,
  withoutPassword,
} from './connection-password';

// Valeurs factices évidentes : aucun mot de passe réel.
const existing = { id: 7, serveur: 'srv', login: 'sa', sqlAuth: true, passwordDefined: true };

describe('connection-password', () => {
  it('retire le mot de passe des bases chargées', () => {
    expect(withoutPassword({ id: 1, password: 'FAKE-PWD' })).toEqual({ id: 1 });
    expect(stripDatabasePasswords([{ id: 1, password: 'FAKE-PWD' }, { id: 2 }])).toEqual([{ id: 1 }, { id: 2 }]);
    expect(stripDatabasePasswords(null)).toEqual([]);
  });

  it('utilise le mot de passe stocké pour une base existante non modifiée', () => {
    expect(usesStoredPassword(existing, false)).toBe(true);
    expect(usesStoredPassword(existing, true)).toBe(false);
    expect(usesStoredPassword({ ...existing, passwordDefined: false }, false)).toBe(false);
    expect(usesStoredPassword({ ...existing, id: 0 }, false)).toBe(false);
    expect(usesStoredPassword({ ...existing, sqlAuth: false }, false)).toBe(false);
  });

  it("n'envoie que l'identifiant de la base tant que le mot de passe n'est pas modifié", () => {
    const auth = discoveryAuth(existing, 'SAGE', false);
    expect(auth).toEqual({ sqlAuth: true, login: 'sa', password: null, connectionId: 7, connectionKind: 'SAGE' });
  });

  it('envoie le mot de passe saisi, sans identifiant de base, une fois modifié', () => {
    const auth = discoveryAuth({ ...existing, password: 'FAKE-NEW' }, 'POINTEUSE', true);
    expect(auth).toEqual({ sqlAuth: true, login: 'sa', password: 'FAKE-NEW' });
    expect('connectionId' in auth).toBe(false);
  });

  it("n'envoie ni login ni mot de passe en authentification Windows", () => {
    expect(discoveryAuth({ ...existing, sqlAuth: false, password: 'FAKE-PWD' }, 'SAGE', true))
      .toEqual({ sqlAuth: false, login: null, password: null });
  });

  it("ne met le mot de passe dans le corps que s'il a été saisi", () => {
    expect(connectionPayload({ ...existing, password: 'FAKE-PWD' }, false)).not.toHaveProperty('password');
    expect(connectionPayload({ ...existing, password: '' }, true)).not.toHaveProperty('password');
    expect(connectionPayload({ ...existing, password: 'FAKE-PWD' }, true)).toHaveProperty('password', 'FAKE-PWD');
    expect(connectionPayload(existing, false)).not.toHaveProperty('passwordDefined');
  });

  it('détecte un changement de serveur, de login ou de mode', () => {
    expect(connectionTargetChanged({ ...existing }, existing)).toBe(false);
    expect(connectionTargetChanged({ ...existing, serveur: ' SRV ' }, existing)).toBe(false);
    expect(connectionTargetChanged({ ...existing, serveur: 'autre' }, existing)).toBe(true);
    expect(connectionTargetChanged({ ...existing, login: 'autre' }, existing)).toBe(true);
    expect(connectionTargetChanged({ ...existing, sqlAuth: false }, existing)).toBe(true);
    expect(connectionTargetChanged(existing, null)).toBe(false);
  });
});
