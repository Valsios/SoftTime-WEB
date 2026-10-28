import { inject } from '@angular/core';
import { CanActivateFn, CanDeactivateFn, Router } from '@angular/router';
import { PrivilegesDraftService } from '../shared/services/privileges-draft.service';
import { SessionStore } from './session.store';

export const authGuard: CanActivateFn = () => {
  const session = inject(SessionStore);
  const router = inject(Router);
  if (session.isAuthenticated()) return true;
  return router.createUrlTree(['/login']);
};

/** Route data: { droit?: number, permission?: string }. Redirects to home when the right or permission is missing. */
export const droitGuard: CanActivateFn = (route) => {
  const session = inject(SessionStore);
  const router = inject(Router);
  const droit = route.data?.['droit'] as number | undefined;
  const permission = route.data?.['permission'] as string | undefined;
  if (droit === undefined && permission === undefined) return true;
  if (session.hasPermission(permission, droit)) return true;
  return router.createUrlTree(['/']);
};

/** Empêche de quitter la page Privilèges avec un brouillon non enregistré. */
export const privilegesLeaveGuard: CanDeactivateFn<unknown> = () => {
  const draft = inject(PrivilegesDraftService);
  if (!draft.hasDirty()) return true;
  return window.confirm('Des modifications de droits ne sont pas enregistrées. Quitter la page quand même ?');
};
