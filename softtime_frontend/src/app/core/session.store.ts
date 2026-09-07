import { Injectable, computed, effect, signal } from '@angular/core';
import { LoginResponse, SageDb } from '../shared/models';

const TOKEN_KEY = 'st_token';
const SESSION_KEY = 'st_session';
const SAGE_KEY = 'st_sage_db';
const POINTEUSE_KEY = 'st_pointeuse_db';
const THEME_KEY = 'st_theme';

interface StoredSession {
  userId: number;
  login: string;
  nom: string;
  roleId: number;
  matricule?: string | null;
  rights: number[];
  authorizedDatabases: SageDb[];
}

@Injectable({ providedIn: 'root' })
export class SessionStore {
  private readonly _token = signal<string | null>(localStorage.getItem(TOKEN_KEY));
  private readonly _session = signal<StoredSession | null>(this.readSession());
  readonly activeSageDb = signal<string | null>(localStorage.getItem(SAGE_KEY));
  readonly activePointeuseDb = signal<string | null>(localStorage.getItem(POINTEUSE_KEY));
  readonly theme = signal<'light' | 'dark'>(
    (localStorage.getItem(THEME_KEY) as 'light' | 'dark') || 'light',
  );

  readonly token = this._token.asReadonly();
  readonly session = this._session.asReadonly();
  readonly isAuthenticated = computed(() => !!this._token());
  readonly rights = computed(() => this._session()?.rights ?? []);
  readonly databases = computed(() => this._session()?.authorizedDatabases ?? []);
  readonly displayName = computed(() => this._session()?.nom ?? this._session()?.login ?? '');

  constructor() {
    effect(() => {
      const t = this.theme();
      document.documentElement.setAttribute('data-theme', t);
      localStorage.setItem(THEME_KEY, t);
    });
  }

  hasRight(droit: number): boolean {
    return this.rights().includes(droit);
  }

  setSession(res: LoginResponse): void {
    const session: StoredSession = {
      userId: res.userId,
      login: res.login,
      nom: res.nom,
      roleId: res.roleId,
      matricule: res.matricule,
      rights: res.rights ?? [],
      authorizedDatabases: res.authorizedDatabases ?? [],
    };
    this._token.set(res.token);
    this._session.set(session);
    localStorage.setItem(TOKEN_KEY, res.token);
    localStorage.setItem(SESSION_KEY, JSON.stringify(session));

    const allowed = (session.authorizedDatabases ?? [])
      .map((d) => d.nomBd)
      .filter((n): n is string => !!n);
    const current = this.activeSageDb();
    if (!current || !allowed.includes(current)) {
      this.setActiveSageDb(allowed[0] ?? null);
    }
  }

  setActiveSageDb(nomBd: string | null): void {
    this.activeSageDb.set(nomBd);
    if (nomBd) localStorage.setItem(SAGE_KEY, nomBd);
    else localStorage.removeItem(SAGE_KEY);
  }

  setActivePointeuseDb(nomBd: string | null): void {
    this.activePointeuseDb.set(nomBd);
    if (nomBd) localStorage.setItem(POINTEUSE_KEY, nomBd);
    else localStorage.removeItem(POINTEUSE_KEY);
  }

  toggleTheme(): void {
    this.theme.update((t) => (t === 'light' ? 'dark' : 'light'));
  }

  clear(): void {
    this._token.set(null);
    this._session.set(null);
    this.activeSageDb.set(null);
    this.activePointeuseDb.set(null);
    localStorage.removeItem(TOKEN_KEY);
    localStorage.removeItem(SESSION_KEY);
    localStorage.removeItem(SAGE_KEY);
    localStorage.removeItem(POINTEUSE_KEY);
  }

  private readSession(): StoredSession | null {
    const raw = localStorage.getItem(SESSION_KEY);
    if (!raw) return null;
    try {
      return JSON.parse(raw) as StoredSession;
    } catch {
      return null;
    }
  }
}
