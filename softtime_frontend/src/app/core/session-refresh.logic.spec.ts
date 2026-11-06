import { describe, expect, it } from 'vitest';
import { isAnonymousAuthRequest, shouldRefreshSession } from './session-refresh.logic';

describe('session-refresh.logic', () => {
  it('les routes de connexion, rafraîchissement et déconnexion sont appelées sans jeton', () => {
    expect(isAnonymousAuthRequest('/api/auth/login')).toBe(true);
    expect(isAnonymousAuthRequest('/api-host/api/auth/refresh')).toBe(true);
    expect(isAnonymousAuthRequest('/api/auth/logout')).toBe(true);
    expect(isAnonymousAuthRequest('/api/auth/me')).toBe(false);
    expect(isAnonymousAuthRequest('/api/shifts')).toBe(false);
  });

  it('un 401 sur un appel métier avec une session ouverte déclenche un rafraîchissement (jeton dans le cookie)', () => {
    expect(shouldRefreshSession('/api/shifts', 401, true)).toBe(true);
    expect(shouldRefreshSession('/api/auth/me', 401, true)).toBe(true);
  });

  it('aucun rafraîchissement sans session, hors 401, ou sur les routes d’authentification', () => {
    expect(shouldRefreshSession('/api/shifts', 401, false)).toBe(false);
    expect(shouldRefreshSession('/api/shifts', 403, true)).toBe(false);
    expect(shouldRefreshSession('/api/auth/login', 401, true)).toBe(false);
    expect(shouldRefreshSession('/api/auth/refresh', 401, true)).toBe(false);
  });
});
