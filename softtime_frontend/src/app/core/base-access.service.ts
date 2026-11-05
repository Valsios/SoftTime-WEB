import { Injectable, Injector, inject } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../shared/services/auth.service';
import { ConfirmService } from './confirm.service';
import { SessionStore } from './session.store';

/**
 * L'accès à une base a changé en cours de session (403 « Vous n'avez pas accès à cette base. ») :
 * propose de recharger la liste des bases (appel /auth/me existant) sans déconnecter ni laisser la page en erreur.
 */
@Injectable({ providedIn: 'root' })
export class BaseAccessService {
  private readonly confirm = inject(ConfirmService);
  private readonly session = inject(SessionStore);
  private readonly router = inject(Router);
  private readonly injector = inject(Injector);
  private asking = false;

  async proposeReload(): Promise<void> {
    if (this.asking) return;                       // plusieurs 403 simultanés : une seule proposition
    this.asking = true;
    try {
      const ok = await this.confirm.ask(
        'Votre accès à cette base a changé. Recharger la liste de vos bases ?',
        'Accès aux bases',
        'Recharger',
      );
      if (!ok) return;
      // AuthService est résolu ici seulement : l'intercepteur qui nous appelle est utilisé par son HttpClient.
      await firstValueFrom(this.injector.get(AuthService).refreshMe());
      if (!this.session.hasUsableDatabase()) {
        await this.router.navigate(['/no-database']);
        return;
      }
      window.location.reload();
    } catch {
      // Échec du rechargement : l'erreur a déjà été signalée par l'intercepteur ; on ne déconnecte pas.
    } finally {
      this.asking = false;
    }
  }
}
