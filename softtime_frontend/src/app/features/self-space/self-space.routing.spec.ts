import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { SessionStore } from '../../core/session.store';
import { AuthService } from '../../shared/services/auth.service';
import { PointeuseDatabasesService } from '../../shared/services/catalog.service';
import { SageDatabasesService } from '../../shared/services/users.service';
import { Shell } from '../../layout/shell';
import { LoginPage } from '../auth/login';

/** Redirection après connexion et entrée de menu « Mon espace » : seul le profil Salarié change de comportement. */
describe('Mon espace : redirection après connexion', () => {
  beforeEach(() => {
    localStorage.clear();
    TestBed.resetTestingModule();
  });
  afterEach(() => {
    localStorage.clear();
    TestBed.resetTestingModule();
  });

  /** Connexion simulée : la session est posée comme le fait AuthService.login, puis la page navigue. */
  function seConnecter(permissions: string[], rights: number[], roleId = 2) {
    const reponse = {
      token: 'jeton', userId: 1, login: 'x', nom: 'X', roleId, matricule: null, rights, authorizedDatabases: [], permissions, permissionsCustomized: true,
    };
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        {
          provide: AuthService,
          useValue: {
            isFirstRun: () => of(false),
            login: () => {
              TestBed.inject(SessionStore).setSession(reponse);
              return of(reponse);
            },
          },
        },
      ],
    });
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    const page = TestBed.createComponent(LoginPage).componentInstance;
    page.login.set('x');
    page.password.set('y');
    page.submit();
    return navigate;
  }

  it('profil Salarié (SELF:READ, aucune porte) : /mon-espace', () => {
    expect(seConnecter(['SELF:READ'], [])).toHaveBeenCalledWith(['/mon-espace']);
  });

  it('administrateur : /dashboard comme avant', () => {
    expect(seConnecter([], [1, 2, 3, 4, 5, 6], 1)).toHaveBeenCalledWith(['/dashboard']);
  });

  it('compte de gestion : /dashboard comme avant', () => {
    expect(seConnecter(['PROC:SHIFTS', 'PROC:PUNCHES'], [3])).toHaveBeenCalledWith(['/dashboard']);
  });

  it('compte sans permission détaillée : /dashboard comme avant', () => {
    expect(seConnecter([], [])).toHaveBeenCalledWith(['/dashboard']);
  });

  it('SELF:READ ajouté à un compte qui a une porte : /dashboard, comportement habituel conservé', () => {
    expect(seConnecter(['SELF:READ', 'PROC:SHIFTS'], [3])).toHaveBeenCalledWith(['/dashboard']);
  });
});

describe('Mon espace : entrée de menu', () => {
  beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();
    if (!window.matchMedia) {
      window.matchMedia = ((q: string) => ({ matches: false, media: q, addEventListener() {}, removeEventListener() {}, addListener() {}, removeListener() {} })) as never;
    }
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: { refreshMe: () => of(undefined), logout: vi.fn() } },
        { provide: PointeuseDatabasesService, useValue: { refresh: () => of([]) } },
        { provide: SageDatabasesService, useValue: { activate: () => of({ cardsAdded: 0, cardsDeactivated: 0, message: '' }) } },
      ],
    });
  });
  afterEach(() => {
    localStorage.clear();
    TestBed.resetTestingModule();
  });

  function monter(permissions: string[], rights: number[], roleId = 2) {
    const store = TestBed.inject(SessionStore);
    store.setSession({
      token: 'jeton', userId: 1, login: 'x', nom: 'Test', roleId, matricule: null, rights, authorizedDatabases: [], permissions, permissionsCustomized: true,
    });
    const fixture = TestBed.createComponent(Shell);
    fixture.detectChanges();
    return { fixture, el: fixture.nativeElement as HTMLElement, store };
  }
  const liens = (el: HTMLElement) => Array.from(el.querySelectorAll('nav.sidebar__nav a')).map((a) => a.getAttribute('href'));

  it('visible avec SELF:READ ; le tableau de bord, inutilisable sans base, n’est pas proposé au profil Salarié', () => {
    const { el } = monter(['SELF:READ'], []);

    expect(liens(el)).toContain('/mon-espace');
    expect(liens(el)).not.toContain('/dashboard');
  });

  it('absente pour un compte de gestion et pour l’administrateur : menu inchangé, tableau de bord conservé', () => {
    for (const [permissions, rights, role] of [[['PROC:SHIFTS'], [3], 2], [[], [1, 2, 3, 4, 5, 6], 1]] as const) {
      const { el } = monter([...permissions], [...rights], role);
      expect(liens(el)).not.toContain('/mon-espace');
      expect(liens(el)).toContain('/dashboard');
      TestBed.resetTestingModule();
      TestBed.configureTestingModule({
        providers: [
          provideRouter([]),
          { provide: AuthService, useValue: { refreshMe: () => of(undefined), logout: vi.fn() } },
          { provide: PointeuseDatabasesService, useValue: { refresh: () => of([]) } },
          { provide: SageDatabasesService, useValue: { activate: () => of({ cardsAdded: 0, cardsDeactivated: 0, message: '' }) } },
        ],
      });
    }
  });

  it('compte de gestion qui porte aussi SELF:READ : l’entrée apparaît mais le tableau de bord reste', () => {
    const { el } = monter(['SELF:READ', 'PROC:SHIFTS'], [3]);

    expect(liens(el)).toContain('/mon-espace');
    expect(liens(el)).toContain('/dashboard');
  });

  it('disparaît à la déconnexion', () => {
    const { fixture, el, store } = monter(['SELF:READ'], []);
    expect(liens(el)).toContain('/mon-espace');

    store.clear();
    TestBed.tick();
    fixture.detectChanges();

    expect(liens(el)).not.toContain('/mon-espace');
  });
});
