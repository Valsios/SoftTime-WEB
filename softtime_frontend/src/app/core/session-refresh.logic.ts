/**
 * Règles du rafraîchissement de session : le jeton d'accès dure 30 minutes, le jeton de rafraîchissement
 * 7 jours (rotation à chaque usage). Un 401 sur un appel métier déclenche un seul rafraîchissement puis
 * la reprise de l'appel ; les routes d'authentification elles-mêmes ne déclenchent jamais de rafraîchissement.
 */

/** Routes appelées sans jeton d'accès (le serveur les accepte anonymes). */
export const ANONYMOUS_AUTH_PATHS = ['/api/v1/auth/login', '/api/v1/auth/refresh', '/api/v1/auth/logout'] as const;

export function isAnonymousAuthRequest(url: string): boolean {
  return ANONYMOUS_AUTH_PATHS.some((path) => url.includes(path));
}

/**
 * Vrai si un 401 sur cette requête doit être tenté de nouveau après un rafraîchissement de session.
 * Le jeton de rafraîchissement est dans un cookie HttpOnly, invisible ici : on se fie à la présence d'une session ouverte.
 */
export function shouldRefreshSession(url: string, status: number, hasSession: boolean): boolean {
  return status === 401 && hasSession && !isAnonymousAuthRequest(url);
}
