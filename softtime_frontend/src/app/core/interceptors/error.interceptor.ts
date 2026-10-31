import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { BaseAccessService } from '../base-access.service';
import { errorMessage, isBaseAccessDenied, isMissingBaseError } from '../error.logic';
import { SessionStore } from '../session.store';
import { ToastService } from '../toast.service';

/** Surfaces API errors as toasts and logs the user out on 401 (except login). */
export const errorInterceptor: HttpInterceptorFn = (req, next) => {
  const session = inject(SessionStore);
  const router = inject(Router);
  const toast = inject(ToastService);
  const baseAccess = inject(BaseAccessService);

  return next(req).pipe(
    catchError((err: HttpErrorResponse) => {
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
          session.clear();
          router.navigate(['/login']);
          toast.error(errorMessage(err) || 'Session expirée. Veuillez vous reconnecter.');
        }
      } else if (isMissingBaseError(err)) {
        // Aucune base active : l'état vide « Aucune base n'est disponible… » remplace l'erreur brute.
        if (!router.url.startsWith('/no-database')) router.navigate(['/no-database']);
      } else if (err.status === 403) {
        toast.error(errorMessage(err));
        if (isBaseAccessDenied(err)) void baseAccess.proposeReload();
      } else if (err.status === 0) {
        toast.error('Serveur API injoignable.');
      } else {
        toast.error(errorMessage(err));
      }
      return throwError(() => err);
    }),
  );
};
