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

  it('un 401 sur un appel métier avec jeton de rafraîchissement déclenche un rafraîchissement', () => {
    expect(shouldRefreshSession('/api/shifts', 401, 'jeton')).toBe(true);
    expect(shouldRefreshSession('/api/auth/me', 401, 'jeton')).toBe(true);
  });

  it('aucun rafraîchissement sans jeton, hors 401, ou sur les routes d’authentification', () => {
    expect(shouldRefreshSession('/api/shifts', 401, null)).toBe(false);
    expect(shouldRefreshSession('/api/shifts', 401, '')).toBe(false);
    expect(shouldRefreshSession('/api/shifts', 403, 'jeton')).toBe(false);
    expect(shouldRefreshSession('/api/auth/login', 401, 'jeton')).toBe(false);
    expect(shouldRefreshSession('/api/auth/refresh', 401, 'jeton')).toBe(false);
  });
});
