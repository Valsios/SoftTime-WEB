import { Injectable } from '@angular/core';
import { Observable, tap } from 'rxjs';
import { ApiService } from '../../core/api';
import { SessionStore } from '../../core/session.store';
import { AdminSetupRequest, AdminSetupResult, LoginRequest, LoginResponse } from '../models';
import { inject } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class AuthService extends ApiService {
  private readonly session = inject(SessionStore);

  login(body: LoginRequest): Observable<LoginResponse> {
    return this.http
      .post<LoginResponse>(this.url('/api/auth/login'), body)
      .pipe(tap((res) => this.session.setSession(res)));
  }

  me(): Observable<LoginResponse> {
    return this.http.get<LoginResponse>(this.url('/api/auth/me'));
  }

  /** Installation vierge (aucun compte) : le client affiche l'écran de création de l'administrateur. */
  isFirstRun(): Observable<boolean> {
    return this.http.get<boolean>(this.url('/api/auth/is-first-run'));
  }

  /** Crée le premier compte administrateur. Le serveur refuse dès qu'un compte existe. */
  createAdmin(body: AdminSetupRequest): Observable<AdminSetupResult> {
    return this.http.post<AdminSetupResult>(this.url('/api/auth/create-admin'), body);
  }

  logout(): void {
    this.session.clear();
  }
}
