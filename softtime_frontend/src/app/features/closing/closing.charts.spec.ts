import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { SessionStore } from '../../core/session.store';
import { SoftDeltaChart } from '../../shared/components/soft-delta-chart';
import { ClosingComparison, ClosingQuery, ClosingReport } from '../../shared/models';
import { CardPaieService, DepartementServiceService } from '../../shared/services/catalog.service';
import { ClosingService } from '../../shared/services/closing.service';
import { ExcelExportService } from '../../shared/services/excel-export.service';
import { ClosingPage } from './closing';
import { comparisonDeltaPoints, scoreDeltaText, severityChartPoints, typeChartPoints } from './closing.logic';

/** Lot C : synthèse visuelle (barres par gravité et par type) et comparaison N-1 en barres divergentes. */

const comparaison: ClosingComparison = {
  from: '2025-10-01T00:00:00',
  to: '2025-10-31T00:00:00',
  scorePrecedent: 90,
  joursEmployeControlesPrecedent: 95,
  ecartScore: 7,
  parType: [
    { type: 'PointageIncomplet', precedent: 0, actuel: 0, ecart: 0 },
    { type: 'PointageManquant', precedent: 10, actuel: 4, ecart: -6 },
    { type: 'QuasiDoublon', precedent: 1, actuel: 3, ecart: 2 },
    { type: 'DoublonExact', precedent: 5, actuel: 5, ecart: 0 },
  ],
};

const rapport = (over: Partial<ClosingReport> = {}): ClosingReport => ({
  from: '2025-11-01T00:00:00',
  to: '2025-11-30T00:00:00',
  departement: null,
  statut: 'ACorriger',
  statutLibelle: 'À corriger',
  seuilBloquant: 0.05,
  score: 97,
  formule: 'formule',
  joursEmployeControles: 100,
  nombreAnomalies: 12,
  parGravite: [
    { cle: 'Information', nombre: 5 },
    { cle: 'Mineure', nombre: 3 },
    { cle: 'Importante', nombre: 0 },
    { cle: 'Critique', nombre: 4 },
  ],
  parType: [
    { cle: 'PointageManquant', nombre: 4 },
    { cle: 'QuasiDoublon', nombre: 3 },
    { cle: 'DoublonExact', nombre: 5 },
    { cle: 'FerieNonApplique', nombre: 0 },
  ],
  anomalies: { items: [], total: 0, page: 1, pageSize: 50, totalPages: 1 },
  parDepartement: [],
  comparaison: null,
  nomsAffiches: true,
  ...over,
});

describe('graphiques du contrôle : logique pure', () => {
  it('par gravité : les quatre niveaux, du plus grave au moins grave, couleur de statut et mêmes nombres que les cartes', () => {
    expect(severityChartPoints(rapport())).toEqual([
      { label: 'Critique', value: 4, color: 'var(--danger)' },
      { label: 'Importante', value: 0, color: 'var(--warning)' },
      { label: 'Mineure', value: 3, color: 'var(--info)' },
      { label: 'Information', value: 5, color: 'var(--text-disabled)' },
    ]);
  });

  it('par type : types présents seulement, du plus fréquent au moins fréquent, libellés au pluriel', () => {
    expect(typeChartPoints(rapport())).toEqual([
      { label: 'Doublons exacts', value: 5 },
      { label: 'Pointages manquants', value: 4 },
      { label: 'Quasi-doublons', value: 3 },
    ]);
  });

  it('écarts N-1 : types présents sur une période au moins, plus fort écart d’abord, signe conservé', () => {
    expect(comparisonDeltaPoints(comparaison)).toEqual([
      { label: 'Pointages manquants', value: -6 },
      { label: 'Quasi-doublons', value: 2 },
      { label: 'Doublons exacts', value: 0 },
    ]);
    expect(comparisonDeltaPoints(null)).toEqual([]);
  });

  it('écart de score en clair, rien sans score comparable', () => {
    expect(scoreDeltaText(comparaison)).toBe('+7 points');
    expect(scoreDeltaText({ ...comparaison, ecartScore: -1 })).toBe('−1 point');
    expect(scoreDeltaText({ ...comparaison, ecartScore: 0 })).toBe('Score identique');
    expect(scoreDeltaText({ ...comparaison, ecartScore: null })).toBe('');
    expect(scoreDeltaText(null)).toBe('');
  });
});

describe('composant partagé soft-delta-chart', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('le sens se lit sans la couleur : côté de l’axe et texte signé', () => {
    const fixture = TestBed.createComponent(SoftDeltaChart);
    fixture.componentRef.setInput('items', [
      { label: 'A', value: 4 },
      { label: 'B', value: -2 },
      { label: 'C', value: 0 },
    ]);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const lignes = Array.from(el.querySelectorAll('.soft-delta__row'));

    expect(lignes.map((l) => l.querySelector('.soft-delta__value')?.textContent?.trim())).toEqual(['+4 de plus', '−2 de moins', 'inchangé']);
    expect(lignes[0].querySelector('.soft-delta__fill--up')?.getAttribute('style')).toContain('width: 100%');
    expect(lignes[1].querySelector('.soft-delta__fill--down')?.getAttribute('style')).toContain('width: 50%');
    expect(lignes[2].querySelector('.soft-delta__fill')).toBeNull();
  });

  it('liste vide : texte calme, aucune barre', () => {
    const fixture = TestBed.createComponent(SoftDeltaChart);
    fixture.componentRef.setInput('emptyText', 'Rien à comparer.');
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Rien à comparer.');
  });
});

describe('page Contrôle avant paie : graphiques', () => {
  afterEach(() => TestBed.resetTestingModule());

  function creer(over: Partial<ClosingReport>) {
    const requetes: ClosingQuery[] = [];
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        {
          provide: SessionStore,
          useValue: {
            hasPermission: vi.fn(() => true),
            token: signal<string | null>('jeton'),
            session: signal<{ userId: number } | null>({ userId: 1 }),
            activeSageDb: signal<string | null>('SAGE_A'),
            activePointeuseDb: signal<string | null>('PTE_A'),
          },
        },
        { provide: CardPaieService, useValue: { list: () => of([]) } },
        { provide: DepartementServiceService, useValue: { lookupBatch: () => of([]) } },
        { provide: ExcelExportService, useValue: { downloadWorkbook: vi.fn() } },
        {
          provide: ClosingService,
          useValue: {
            checklist: () => of({ from: '', to: '', departement: null, lignes: [] }),
            analysis: (q: ClosingQuery) => {
              requetes.push(q);
              return of(rapport(over));
            },
          },
        },
      ],
    });
    const fixture = TestBed.createComponent(ClosingPage);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    Array.from(el.querySelectorAll('button')).find((b) => b.textContent?.trim() === 'Analyser')!.click();
    fixture.detectChanges();
    return { el, requetes };
  }

  it('deux graphiques de répartition, libellés en texte, sans requête supplémentaire', () => {
    const { el, requetes } = creer({});
    const section = el.querySelector('section.closing-charts')!;

    expect(section.getAttribute('aria-label')).toBe('Répartition des anomalies');
    expect(section.querySelectorAll('soft-bar-chart')).toHaveLength(2);
    expect(section.textContent).toContain('Critique');
    expect(section.textContent).toContain('Doublons exacts');
    expect(requetes).toHaveLength(1);
  });

  it('aucun graphique de répartition sans anomalie ni sans jour contrôlé', () => {
    expect(creer({ nombreAnomalies: 0 }).el.querySelector('section.closing-charts')).toBeNull();
    TestBed.resetTestingModule();
    expect(creer({ joursEmployeControles: 0 }).el.querySelector('section.closing-charts')).toBeNull();
  });

  it('comparaison : carte d’évolution avec le score avant/après et les écarts signés ; absente sans comparaison', () => {
    const { el } = creer({ comparaison });
    const delta = el.querySelector('soft-delta-chart')!;

    expect(delta).not.toBeNull();
    expect(el.querySelector('.closing-score-delta')?.textContent?.replace(/\s+/g, ' ')).toContain('90 → 97');
    expect(el.querySelector('.closing-score-delta')?.textContent).toContain('+7 points');
    expect(delta.textContent).toContain('−6 de moins');

    TestBed.resetTestingModule();
    expect(creer({}).el.querySelector('soft-delta-chart')).toBeNull();
  });
});
