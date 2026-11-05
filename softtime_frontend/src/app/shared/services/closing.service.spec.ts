import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, describe, expect, it } from 'vitest';
import { ClosingService } from './closing.service';

/** Paramètres réellement envoyés à GET api/closing/analysis. */
describe('ClosingService : paramètres de gravité', () => {
  afterEach(() => TestBed.resetTestingModule());

  function envoyer(query: Parameters<ClosingService['analysis']>[0]) {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    const http = TestBed.inject(HttpTestingController);
    TestBed.inject(ClosingService).analysis(query).subscribe();
    const requete = http.expectOne((r) => r.url.endsWith('/api/closing/analysis'));
    return requete.request.params;
  }

  it('sans niveau ni tri : aucun des deux paramètres n’est envoyé (requête identique à l’ancienne)', () => {
    for (const query of [{ from: '2025-11-01' }, { from: '2025-11-01', gravites: [] as string[], tri: undefined }]) {
      TestBed.resetTestingModule();
      const params = envoyer(query);
      expect(params.has('gravites')).toBe(false);
      expect(params.has('tri')).toBe(false);
    }
  });

  it('avec niveaux et tri : gravites=Critique,Importante et tri=gravite', () => {
    const params = envoyer({ from: '2025-11-01', gravites: ['Critique', 'Importante'], tri: 'gravite' });
    expect(params.get('gravites')).toBe('Critique,Importante');
    expect(params.get('tri')).toBe('gravite');
  });
});
