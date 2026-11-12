import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, describe, expect, it } from 'vitest';
import { ClosingReport } from '../../shared/models';
import { ClosingService } from '../../shared/services/closing.service';
import { exportSummary, listFilterText, toggleType, typeFilterText } from './closing.logic';

/** Filtre de la liste par TYPE d'anomalie (lot A1) : bascule, libellés, paramètre envoyé, synthèse d'export. */
describe('filtre par type de la liste des anomalies', () => {
  it('toggleType ajoute puis retire, en gardant l’ordre des types du serveur', () => {
    let types = toggleType([], 'QuasiDoublon');
    expect(types).toEqual(['QuasiDoublon']);
    types = toggleType(types, 'PointageManquant');
    expect(types).toEqual(['PointageManquant', 'QuasiDoublon']); // ordre de l'énumération, pas du clic
    types = toggleType(types, 'QuasiDoublon');
    expect(types).toEqual(['PointageManquant']);
  });

  it('typeFilterText et listFilterText : libellés lisibles, vides sans filtre', () => {
    expect(typeFilterText([])).toBe('');
    expect(typeFilterText(['PointageManquant', 'QuasiDoublon'])).toBe('Types : Pointages manquants, Quasi-doublons');
    expect(listFilterText([], [])).toBe('');
    expect(listFilterText(['Critique'], [])).toBe('Filtre de gravité : Critique');
    expect(listFilterText(['Critique'], ['DoublonExact'])).toBe('Filtre de gravité : Critique · Types : Doublons exacts');
  });

  it("la synthèse d'export nomme le filtre de type (ou son absence)", () => {
    const report = {
      from: '2025-11-01',
      to: '2025-11-30',
      statut: 'Pret',
      statutLibelle: 'Prêt',
      score: 100,
      joursEmployeControles: 1,
      nombreAnomalies: 0,
      parGravite: [],
      parType: [],
    } as unknown as ClosingReport;
    const filter = { from: '2025-11-01', to: '2025-11-30', departement: null, includeComparison: false };

    const sans = exportSummary(report, filter, new Date(2025, 10, 1), 0);
    const avec = exportSummary(report, filter, new Date(2025, 10, 1), 0, {
      gravites: [], tri: 'date', includeInformation: false, types: ['QuasiDoublon'],
    });

    expect(sans.rows).toContainEqual(['Filtre de type', 'Aucun (tous les types)']);
    expect(avec.rows).toContainEqual(['Filtre de type', 'Quasi-doublons']);
  });
});

describe('ClosingService : paramètre types', () => {
  afterEach(() => TestBed.resetTestingModule());

  function envoyer(query: Parameters<ClosingService['analysis']>[0]) {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    const http = TestBed.inject(HttpTestingController);
    TestBed.inject(ClosingService).analysis(query).subscribe();
    return http.expectOne((r) => r.url.endsWith('/api/v1/closing/analysis')).request.params;
  }

  it('sans type : le paramètre n’est pas envoyé (requête identique à l’ancienne)', () => {
    for (const query of [{ from: '2025-11-01' }, { from: '2025-11-01', types: [] as string[] }]) {
      TestBed.resetTestingModule();
      expect(envoyer(query).has('types')).toBe(false);
    }
  });

  it('avec types : types=DoublonExact,QuasiDoublon', () => {
    const params = envoyer({ from: '2025-11-01', types: ['DoublonExact', 'QuasiDoublon'] });
    expect(params.get('types')).toBe('DoublonExact,QuasiDoublon');
  });
});
