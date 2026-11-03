import { HttpBackend, HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, finalize, map, shareReplay, tap, throwError } from 'rxjs';
import { environment } from '../../environments/environment';
import { LoginResponse } from '../shared/models';
import { SessionStore } from './session.store';

/**
 * Rafraîchit la session avec le jeton de rafraîchissement (rotation côté serveur).
 * Un seul appel à la fois : les requêtes qui reçoivent un 401 en même temps attendent le même résultat.
 * Le client HTTP passe par HttpBackend : pas d'intercepteur, donc ni boucle de rafraîchissement ni message d'erreur.
 */
@Injectable({ providedIn: 'root' })
export class TokenRefreshService {
  private readonly http = new HttpClient(inject(HttpBackend));
  private readonly session = inject(SessionStore);
  private inFlight: Observable<string> | null = null;

  /** Nouveau jeton d'accès ; échoue si la session ne peut pas être prolongée (reconnexion nécessaire). */
  refresh(): Observable<string> {
    const refreshToken = this.session.refreshToken();
    if (!refreshToken) return throwError(() => new Error('Aucun jeton de rafraîchissement.'));
    this.inFlight ??= this.http
      .post<LoginResponse>(`${environment.apiBaseUrl}/api/auth/refresh`, { refreshToken })
      .pipe(
        tap((res) => this.session.setSession(res)),
        map((res) => res.token),
        finalize(() => (this.inFlight = null)),
        shareReplay({ bufferSize: 1, refCount: false }),
      );
    return this.inFlight;
  }

  /** Révoque le jeton de rafraîchissement côté serveur ; une erreur réseau n'empêche pas la déconnexion locale. */
  revoke(): void {
    const refreshToken = this.session.refreshToken();
    if (!refreshToken) return;
    this.http.post(`${environment.apiBaseUrl}/api/auth/logout`, { refreshToken }).subscribe({ error: () => undefined });
  }
}
