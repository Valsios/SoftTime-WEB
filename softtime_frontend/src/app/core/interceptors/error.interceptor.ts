import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { Observable, catchError, switchMap, throwError } from 'rxjs';
import { BaseAccessService } from '../base-access.service';
import { errorMessage, isBaseAccessDenied, isMissingBaseError, previewFromConflict } from '../error.logic';
import { shouldRefreshSession } from '../session-refresh.logic';
import { SessionStore } from '../session.store';
import { ToastService } from '../toast.service';
import { TokenRefreshService } from '../token-refresh.service';

const SESSION_EXPIRED = 'Session expirée. Veuillez vous reconnecter.';

/**
 * Surfaces API errors as toasts. On 401 the session is first refreshed once (rotating refresh token) and the
 * request replayed with the new access token; the user is logged out only when the refresh itself fails.
 */
export const errorInterceptor: HttpInterceptorFn = (req, next) => {
  const session = inject(SessionStore);
  const router = inject(Router);
  const toast = inject(ToastService);
  const baseAccess = inject(BaseAccessService);
  const refresher = inject(TokenRefreshService);

  const expire = (message?: string): void => {
    session.clear();
    router.navigate(['/login']);
    toast.error(message || SESSION_EXPIRED);
  };

  const handle = (err: HttpErrorResponse): Observable<never> => {
    // La politique de mot de passe a un repli local : inutile d'alerter l'utilisateur.
    if (req.url.includes('/api/auth/password-policy')) {
      return throwError(() => err);
    }
    const isLogin = req.url.includes('/api/auth/login');
    if (err.status === 401) {
      if (isLogin) {
        const msg = errorMessage(err);
        toast.error(msg.includes('Erreur interne') ? 'Identifiants invalides.' : msg);
      } else {
        expire(errorMessage(err));
      }
    } else if (isMissingBaseError(err)) {
      // Aucune base active : l'état vide « Aucune base n'est disponible… » remplace l'erreur brute.
      if (!router.url.startsWith('/no-database')) router.navigate(['/no-database']);
    } else if (previewFromConflict(err)) {
      // Confirmation demandée par la page (changement de rôle) : pas une erreur à afficher.
    } else if (err.status === 403) {
      toast.error(errorMessage(err));
      if (isBaseAccessDenied(err)) void baseAccess.proposeReload();
    } else if (err.status === 429) {
      toast.error(errorMessage(err) || 'Trop de tentatives. Patientez une minute avant de réessayer.');
    } else if (err.status === 0) {
      toast.error('Serveur API injoignable.');
    } else {
      toast.error(errorMessage(err));
    }
    return throwError(() => err);
  };

  return next(req).pipe(
    catchError((err: HttpErrorResponse) => {
      if (!shouldRefreshSession(req.url, err.status, session.isAuthenticated())) {
        return handle(err);
      }
      return refresher.refresh().pipe(
        catchError(() => {
          expire();
          return throwError(() => err);
        }),
        switchMap((token) =>
          next(req.clone({ setHeaders: { Authorization: `Bearer ${token}` } })).pipe(catchError(handle)),
        ),
      );
    }),
  );
};
