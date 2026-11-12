import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { RightsRefreshService } from './rights-refresh.service';
import { SessionStore } from './session.store';
import { TokenRefreshService } from './token-refresh.service';

/** Rafraîchissement des droits : après un 403 et à la navigation, jamais de déconnexion. */
describe('RightsRefreshService', () => {
  afterEach(() => TestBed.resetTestingModule());

  function service(refresh: () => unknown, authenticated = true) {
    const clear = vi.fn();
    TestBed.configureTestingModule({
      providers: [
        { provide: TokenRefreshService, useValue: { refresh } },
        { provide: SessionStore, useValue: { isAuthenticated: () => authenticated, clear } },
      ],
    });
    return { sut: TestBed.inject(RightsRefreshService), clear };
  }

  it('après un 403 : un seul rafraîchissement pour une rafale de refus', () => {
    const refresh = vi.fn(() => of('jeton'));
    const { sut } = service(refresh);
    const t = Date.now() + 60_000;
    sut.afterForbidden(t);
    sut.afterForbidden(t + 1_000);
    expect(refresh).toHaveBeenCalledTimes(1);
  });

  it('navigation : pas de relecture moins de 5 minutes après la précédente', () => {
    const refresh = vi.fn(() => of('jeton'));
    const { sut } = service(refresh);
    sut.onNavigation(Date.now() + 60_000);
    expect(refresh).not.toHaveBeenCalled();
    sut.onNavigation(Date.now() + 6 * 60_000);
    expect(refresh).toHaveBeenCalledTimes(1);
  });

  it('échec du rafraîchissement : ignoré, aucune déconnexion', () => {
    const refresh = vi.fn(() => throwError(() => new Error('refus')));
    const { sut, clear } = service(refresh);
    sut.afterForbidden(Date.now() + 60_000);
    expect(refresh).toHaveBeenCalledTimes(1);
    expect(clear).not.toHaveBeenCalled();
  });

  it('compte non connecté : rien n’est relu', () => {
    const refresh = vi.fn(() => of('jeton'));
    const { sut } = service(refresh, false);
    sut.afterForbidden(Date.now() + 60_000);
    expect(refresh).not.toHaveBeenCalled();
  });
});
