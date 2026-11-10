import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, describe, expect, it } from 'vitest';
import { ClosingService } from './closing.service';

/** Paramètres réellement envoyés à GET api/v1/closing/analysis. */
describe('ClosingService : paramètres de gravité', () => {
  afterEach(() => TestBed.resetTestingModule());

  function envoyer(query: Parameters<ClosingService['analysis']>[0]) {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    const http = TestBed.inject(HttpTestingController);
    TestBed.inject(ClosingService).analysis(query).subscribe();
    const requete = http.expectOne((r) => r.url.endsWith('/api/v1/closing/analysis'));
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

describe('ClosingService : checklist', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('GET api/v1/closing/checklist avec la période et le département, sans paramètre d’analyse', () => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    const http = TestBed.inject(HttpTestingController);
    TestBed.inject(ClosingService).checklist({ from: '2025-11-01', to: '2025-11-30', departement: 'RH' }).subscribe();

    const requete = http.expectOne((r) => r.url.endsWith('/api/v1/closing/checklist'));
    expect(requete.request.method).toBe('GET');
    expect(requete.request.params.get('from')).toBe('2025-11-01T00:00:00Z');
    expect(requete.request.params.get('to')).toBe('2025-11-30T00:00:00Z');
    expect(requete.request.params.get('departement')).toBe('RH');
    expect(requete.request.params.has('gravites')).toBe(false);
    expect(requete.request.params.has('page')).toBe(false);
  });
});
