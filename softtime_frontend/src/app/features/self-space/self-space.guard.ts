import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { SessionStore } from '../../core/session.store';
import { hasSelfPermission } from './self-space.logic';

/**
 * Route /mon-espace : réservée aux comptes qui portent self.read. Ce n'est qu'un confort d'affichage : le serveur refuse de toute
 * façon (404 si la fonction est coupée, 403 sans la permission ou sans matricule valide).
 */
export const selfSpaceGuard: CanActivateFn = () => {
  const session = inject(SessionStore);
  const router = inject(Router);
  return hasSelfPermission(session.permissions()) ? true : router.createUrlTree(['/']);
};
