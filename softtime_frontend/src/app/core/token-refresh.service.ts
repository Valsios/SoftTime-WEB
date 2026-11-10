import { HttpBackend, HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, finalize, map, shareReplay, tap } from 'rxjs';
import { environment } from '../../environments/environment';
import { LoginResponse } from '../shared/models';
import { unwrapApiBody } from './api-envelope';
import { SessionStore } from './session.store';

/**
 * Rafraîchit la session (rotation côté serveur). Le jeton de rafraîchissement est dans un cookie HttpOnly que le
 * navigateur envoie seul : ce code ne le voit jamais. Un seul appel à la fois : les requêtes qui reçoivent un 401 en
 * même temps attendent le même résultat. Le client HTTP passe par HttpBackend : pas d'intercepteur, donc ni boucle de
 * rafraîchissement ni message d'erreur.
 */
@Injectable({ providedIn: 'root' })
export class TokenRefreshService {
  private readonly http = new HttpClient(inject(HttpBackend));
  private readonly session = inject(SessionStore);
  private inFlight: Observable<string> | null = null;

  /** Nouveau jeton d'accès ; échoue si la session ne peut pas être prolongée (reconnexion nécessaire). */
  refresh(): Observable<string> {
    this.inFlight ??= this.http
      .post<unknown>(`${environment.apiBaseUrl}/api/v1/auth/refresh`, {}, { withCredentials: true })
      .pipe(
        // HttpBackend contourne l'intercepteur qui déballe l'enveloppe { success, message, data } : on la déballe ici.
        map((body) => unwrapApiBody(body) as LoginResponse),
        tap((res) => {
          if (!res?.token) throw new Error('Réponse de rafraîchissement sans jeton.');
          this.session.setSession(res);
        }),
        map((res) => res.token),
        finalize(() => (this.inFlight = null)),
        shareReplay({ bufferSize: 1, refCount: false }),
      );
    return this.inFlight;
  }

  /** Révoque le jeton de rafraîchissement côté serveur et efface le cookie ; une erreur réseau n'empêche pas la déconnexion locale. */
  revoke(): void {
    this.http
      .post(`${environment.apiBaseUrl}/api/v1/auth/logout`, {}, { withCredentials: true })
      .subscribe({ error: () => undefined });
  }
}
