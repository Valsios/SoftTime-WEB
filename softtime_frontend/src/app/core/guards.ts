import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { SessionStore } from './session.store';

export const authGuard: CanActivateFn = () => {
  const session = inject(SessionStore);
  const router = inject(Router);
  if (session.isAuthenticated()) return true;
  return router.createUrlTree(['/login']);
};

/** Route data: { droit: number }. Redirects to home when the right is missing. */
export const droitGuard: CanActivateFn = (route) => {
  const session = inject(SessionStore);
  const router = inject(Router);
  const droit = route.data?.['droit'] as number | undefined;
  if (droit === undefined || session.hasRight(droit)) return true;
  return router.createUrlTree(['/']);
};
