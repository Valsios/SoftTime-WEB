import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { SessionStore } from '../session.store';
import { ToastService } from '../toast.service';

function apiMessage(err: HttpErrorResponse): string {
  return (
    (err.error && (err.error.error || err.error.message || err.error.title)) ||
    err.message ||
    'Une erreur est survenue.'
  );
}

/** Surfaces API errors as toasts and logs the user out on 401 (except login). */
export const errorInterceptor: HttpInterceptorFn = (req, next) => {
  const session = inject(SessionStore);
  const router = inject(Router);
  const toast = inject(ToastService);

  return next(req).pipe(
    catchError((err: HttpErrorResponse) => {
      const isLogin = req.url.includes('/api/auth/login');
      if (err.status === 401) {
        if (isLogin) {
          const msg = apiMessage(err);
          toast.error(msg.includes('Erreur interne') ? 'Identifiants invalides.' : msg);
        } else {
          session.clear();
          router.navigate(['/login']);
          toast.error(apiMessage(err) || 'Session expirée. Veuillez vous reconnecter.');
        }
      } else if (err.status === 403) {
        toast.error(apiMessage(err) || "Accès refusé : droits insuffisants ou base non autorisée.");
      } else if (err.status === 0) {
        toast.error('Serveur API injoignable.');
      } else {
        toast.error(apiMessage(err));
      }
      return throwError(() => err);
    }),
  );
};
