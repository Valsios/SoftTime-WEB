import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ClosingSessionState } from '../features/closing/closing.state';
import { SessionStore } from '../core/session.store';
import { ClosingReport } from '../shared/models';
import { AuthService } from '../shared/services/auth.service';
import { PointeuseDatabasesService } from '../shared/services/catalog.service';
import { SageDatabasesService } from '../shared/services/users.service';
import { Shell } from './shell';

/** Badge « Contrôle avant paie » de la barre du haut : lecture de l'état de session seulement, jamais de requête. */
describe('Shell : badge du Contrôle avant paie', () => {
  const refreshMe = vi.fn(() => of(undefined));
  const refreshPointeuse = vi.fn(() => of(undefined));

  const rapport = (critiques: number): ClosingReport =>
    ({
      parGravite: [
        { cle: 'Information', nombre: 2 },
        { cle: 'Mineure', nombre: 1 },
        { cle: 'Importante', nombre: 4 },
        { cle: 'Critique', nombre: critiques },
      ],
    }) as unknown as ClosingReport;

  function seConnecter(permissions: string[]) {
    const store = TestBed.inject(SessionStore);
    store.setSession({
      token: 'jeton',
      userId: 1,
      login: 'test',
      nom: 'Utilisateur Test',
      roleId: 2,   // non administrateur : l’administrateur a toutes les permissions
      matricule: null,
      rights: [6],
      authorizedDatabases: [{ nomBd: 'SAGE_A' }] as never,
      permissions,
      permissionsCustomized: true,
    });
    store.setActiveSageDb('SAGE_A');
    return store;
  }

  function analyser(critiques: number) {
    const etat = TestBed.inject(ClosingSessionState);
    etat.storeResult(
      {
        filter: { from: '2025-11-01', to: '2025-11-30', departement: null, includeComparison: false },
        report: rapport(critiques),
        analysedAt: new Date(),
      },
      etat.currentContext(),
    );
  }

  function monter() {
    const fixture = TestBed.createComponent(Shell);
    fixture.detectChanges();
    return { fixture, el: fixture.nativeElement as HTMLElement };
  }

  const badge = (el: HTMLElement) => el.querySelector<HTMLAnchorElement>('a.topbar__closing');

  beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();
    refreshMe.mockClear();
    refreshPointeuse.mockClear();
    if (!window.matchMedia) {
      window.matchMedia = ((q: string) => ({ matches: false, media: q, addEventListener() {}, removeEventListener() {}, addListener() {}, removeListener() {} })) as never;
    }
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: { refreshMe, logout: vi.fn() } },
        { provide: PointeuseDatabasesService, useValue: { refresh: refreshPointeuse } },
        { provide: SageDatabasesService, useValue: { activate: () => of({ cardsAdded: 0, cardsDeactivated: 0, message: '' }) } },
      ],
    });
  });

  afterEach(() => {
    TestBed.resetTestingModule();
    localStorage.clear();
  });

  it('aucun badge tant qu’aucune analyse n’existe en session', () => {
    seConnecter(['REPORTS:CLOSING']);
    const { el } = monter();

    expect(badge(el)).toBeNull();
  });

  it('avec la permission et une analyse : le nombre d’anomalies critiques, un libellé lisible et un lien vers la page', () => {
    seConnecter(['REPORTS:CLOSING']);
    analyser(3);
    const { el } = monter();

    const lien = badge(el)!;
    expect(lien).not.toBeNull();
    expect(lien.getAttribute('href')).toBe('/closing');
    expect(lien.getAttribute('aria-label')).toBe('Contrôle avant paie : 3 anomalies critiques');
    expect(lien.querySelector('.topbar__closing-count')?.textContent?.trim()).toBe('3');
    expect(lien.classList.contains('topbar__closing--alert')).toBe(true);
  });

  it('libellés au singulier et à zéro, sans alerte visuelle à zéro', () => {
    seConnecter(['REPORTS:CLOSING']);
    analyser(1);
    const un = monter();
    expect(badge(un.el)!.getAttribute('aria-label')).toBe('Contrôle avant paie : 1 anomalie critique');

    TestBed.inject(ClosingSessionState).reset();
    analyser(0);
    un.fixture.detectChanges();
    expect(badge(un.el)!.getAttribute('aria-label')).toBe('Contrôle avant paie : aucune anomalie critique');
    expect(badge(un.el)!.classList.contains('topbar__closing--alert')).toBe(false);
  });

  it('sans la permission REPORTS:CLOSING : jamais de badge, même avec une analyse en mémoire', () => {
    seConnecter(['PROC:ANOMALIES']);
    analyser(5);
    const { el } = monter();

    expect(badge(el)).toBeNull();
  });

  it('disparaît à la déconnexion', () => {
    const store = seConnecter(['REPORTS:CLOSING']);
    analyser(2);
    const { fixture, el } = monter();
    expect(badge(el)).not.toBeNull();

    store.clear();
    TestBed.tick();
    fixture.detectChanges();

    expect(badge(el)).toBeNull();
  });

  it('disparaît au changement de base active', () => {
    const store = seConnecter(['REPORTS:CLOSING']);
    analyser(2);
    const { fixture, el } = monter();
    expect(badge(el)).not.toBeNull();

    store.activeSageDb.set('SAGE_B');
    TestBed.tick();
    fixture.detectChanges();

    expect(badge(el)).toBeNull();
  });

  it('aucune requête pour le badge : ni à l’apparition, ni au fil du temps', () => {
    seConnecter(['REPORTS:CLOSING']);
    const { fixture, el } = monter();
    const avant = [refreshMe.mock.calls.length, refreshPointeuse.mock.calls.length];

    analyser(4);
    fixture.detectChanges();
    TestBed.tick();
    fixture.detectChanges();

    expect(badge(el)).not.toBeNull();
    expect([refreshMe.mock.calls.length, refreshPointeuse.mock.calls.length]).toEqual(avant);   // seuls les appels existants du démarrage
  });
});
