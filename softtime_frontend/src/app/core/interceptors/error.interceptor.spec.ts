import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { of } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { CardPaieService, ClockParamsService, DepartementServiceService } from '../../shared/services/catalog.service';
import { BaseAccessService } from '../base-access.service';
import { RightsRefreshService } from '../rights-refresh.service';
import { SessionStore } from '../session.store';
import { silentContext } from '../silent-request';
import { ToastService } from '../toast.service';
import { TokenRefreshService } from '../token-refresh.service';
import { errorInterceptor } from './error.interceptor';

const GATE_MESSAGE = "Vous n'avez pas accès à cette section.";

describe('errorInterceptor : requêtes optionnelles silencieuses', () => {
  let http: HttpClient;
  let ctrl: HttpTestingController;
  let toast: { error: ReturnType<typeof vi.fn> };
  let rights: { afterForbidden: ReturnType<typeof vi.fn> };
  let session: { isAuthenticated: () => boolean; clear: ReturnType<typeof vi.fn>; activeSageDb: () => null };
  let router: { url: string; navigate: ReturnType<typeof vi.fn> };

  beforeEach(() => {
    toast = { error: vi.fn() };
    rights = { afterForbidden: vi.fn() };
    session = { isAuthenticated: () => false, clear: vi.fn(), activeSageDb: () => null };
    router = { url: '/corrected-hours', navigate: vi.fn() };
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([errorInterceptor])),
        provideHttpClientTesting(),
        { provide: ToastService, useValue: toast },
        { provide: RightsRefreshService, useValue: rights },
        { provide: SessionStore, useValue: session },
        { provide: Router, useValue: router },
        { provide: BaseAccessService, useValue: { proposeReload: vi.fn() } },
        { provide: TokenRefreshService, useValue: { refresh: () => of('t') } },
      ],
    });
    http = TestBed.inject(HttpClient);
    ctrl = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    ctrl.verify();
    TestBed.resetTestingModule();
  });

  function refuser(url: string, status = 403, body: object = { success: false, error: GATE_MESSAGE }) {
    ctrl.expectOne((r) => r.url.endsWith(url)).flush(body, { status, statusText: 'x' });
  }

  it('action de l’utilisateur refusée (403) : le toast reste affiché et les droits sont relus', () => {
    const erreur = vi.fn();
    http.post('/api/v1/card-paie', {}).subscribe({ error: erreur });
    refuser('/api/v1/card-paie');
    expect(toast.error).toHaveBeenCalledWith(GATE_MESSAGE);
    expect(rights.afterForbidden).toHaveBeenCalled();
    expect(erreur).toHaveBeenCalled();
  });

  it.each([403, 500, 0])('requête silencieuse en échec (%i) : aucun toast, l’erreur est rendue à l’appelant', (status) => {
    const erreur = vi.fn();
    http.get('/api/v1/card-paie', { context: silentContext() }).subscribe({ error: erreur });
    refuser('/api/v1/card-paie', status);
    expect(toast.error).not.toHaveBeenCalled();
    expect(rights.afterForbidden).not.toHaveBeenCalled();
    expect(router.navigate).not.toHaveBeenCalled();
    expect(erreur).toHaveBeenCalled();
  });

  it('requête silencieuse en 401 sans session : la déconnexion reste traitée', () => {
    http.get('/api/v1/card-paie', { context: silentContext() }).subscribe({ error: () => undefined });
    refuser('/api/v1/card-paie', 401, { success: false, error: 'Session expirée.' });
    expect(session.clear).toHaveBeenCalled();
    expect(router.navigate).toHaveBeenCalledWith(['/login']);
  });

  it('lectures de confort des services (CardPaie, recherche SAGE, mode de pointage) : 403 sans toast', () => {
    TestBed.inject(CardPaieService).list({ silent: true }).subscribe({ error: () => undefined });
    TestBed.inject(DepartementServiceService).lookupBatch(['1']).subscribe({ error: () => undefined });
    TestBed.inject(ClockParamsService).get({ silent: true }).subscribe({ error: () => undefined });
    refuser('/api/v1/card-paie');
    refuser('/api/v1/departement-service/lookup-batch');
    refuser('/api/v1/clock-params');
    expect(toast.error).not.toHaveBeenCalled();
  });

  it('écran CardPaie (lecture normale) : un refus reste affiché', () => {
    TestBed.inject(CardPaieService).list().subscribe({ error: () => undefined });
    refuser('/api/v1/card-paie');
    expect(toast.error).toHaveBeenCalledWith(GATE_MESSAGE);
  });
});
