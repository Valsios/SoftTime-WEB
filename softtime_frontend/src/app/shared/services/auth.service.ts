import { Injectable } from '@angular/core';
import { Observable, tap } from 'rxjs';
import { ApiService } from '../../core/api';
import { SessionStore } from '../../core/session.store';
import { LoginRequest, LoginResponse } from '../models';
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

  logout(): void {
    this.session.clear();
  }
}
