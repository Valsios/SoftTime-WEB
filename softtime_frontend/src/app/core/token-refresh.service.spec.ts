import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { ClosingService } from '../shared/services/closing.service';
import { SessionStore } from './session.store';
import { TokenRefreshService } from './token-refresh.service';

/**
 * Les clients HttpBackend contournent l'intercepteur qui déballe l'enveloppe { success, message, data } de l'API :
 * ils doivent la déballer eux-mêmes. Sans cela, un rechargement de page (changement de base active) rejouait les
 * appels avec « Bearer undefined » et déconnectait l'utilisateur.
 */
const enveloppe = <T>(data: T) => ({ success: true, message: 'OK', data, timestamp: '2026-11-09T11:20:36Z' });

describe('Clients sans intercepteur : réponses enveloppées', () => {
  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
  });
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
    TestBed.resetTestingModule();
    localStorage.clear();
  });

  it('refresh : le jeton est lu dans « data » et la session est rétablie', () => {
    const http = TestBed.inject(HttpTestingController);
    let jeton: string | undefined;

    TestBed.inject(TokenRefreshService).refresh().subscribe((t) => (jeton = t));
    http.expectOne((r) => r.url.endsWith('/api/v1/auth/refresh')).flush(enveloppe({
      token: 'jeton-neuf', userId: 14, login: 'admin', nom: 'Administrateur', roleId: 1, rights: [1],
      authorizedDatabases: [], permissions: [], permissionsCustomized: false,
    }));

    expect(jeton).toBe('jeton-neuf');
    expect(TestBed.inject(SessionStore).token()).toBe('jeton-neuf');
    expect(TestBed.inject(SessionStore).session()?.login).toBe('admin');
  });

  it('refresh : une réponse sans jeton est un échec, jamais un « Bearer undefined »', () => {
    const http = TestBed.inject(HttpTestingController);
    let erreur: unknown;

    TestBed.inject(TokenRefreshService).refresh().subscribe({ error: (e) => (erreur = e) });
    http.expectOne((r) => r.url.endsWith('/api/v1/auth/refresh')).flush(enveloppe({ login: 'admin' }));

    expect(erreur).toBeInstanceOf(Error);
    expect(TestBed.inject(SessionStore).token()).toBeNull();
  });

  it('analysisSilently : le rapport est lu dans « data »', () => {
    const http = TestBed.inject(HttpTestingController);
    let statut: string | null | undefined;

    TestBed.inject(ClosingService).analysisSilently({ from: '2025-11-01' }).subscribe((r) => (statut = r.statut));
    http.expectOne((r) => r.url.endsWith('/api/v1/closing/analysis')).flush(enveloppe({ statut: 'Bloquant' }));

    expect(statut).toBe('Bloquant');
  });
});
