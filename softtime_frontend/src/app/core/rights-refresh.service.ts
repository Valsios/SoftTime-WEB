import { Injectable, inject } from '@angular/core';
import { FORBIDDEN_REFRESH_MS, NAVIGATION_REFRESH_MS, isRefreshDue } from './rights-refresh.logic';
import { SessionStore } from './session.store';
import { TokenRefreshService } from './token-refresh.service';

/**
 * Relit les droits effectifs (nouveau jeton : portes, permissions, bases) pour corriger menus et boutons sans attendre
 * l'expiration du jeton. Le serveur reste l'autorité : un menu en retard est un défaut d'affichage, pas de sécurité.
 * Jamais de déconnexion ici : un échec est ignoré, le prochain 401 suit le parcours habituel.
 */
@Injectable({ providedIn: 'root' })
export class RightsRefreshService {
  private readonly refresher = inject(TokenRefreshService);
  private readonly session = inject(SessionStore);
  private lastRefreshAt: number | null = Date.now();

  /** Après un 403 : les droits ont peut-être changé (menu ou bouton en retard). */
  afterForbidden(now = Date.now()): void {
    this.refreshIfDue(now, FORBIDDEN_REFRESH_MS);
  }

  /** À chaque navigation : relecture si la dernière date de plus de 5 minutes. */
  onNavigation(now = Date.now()): void {
    this.refreshIfDue(now, NAVIGATION_REFRESH_MS);
  }

  private refreshIfDue(now: number, intervalMs: number): void {
    if (!this.session.isAuthenticated() || !isRefreshDue(this.lastRefreshAt, now, intervalMs)) return;
    this.lastRefreshAt = now;
    this.refresher.refresh().subscribe({ error: () => undefined });
  }
}
