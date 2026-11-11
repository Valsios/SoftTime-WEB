import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { SessionStore } from '../../core/session.store';
import { ClosingAnomalyView, ClosingQuery, ClosingReport } from '../../shared/models';
import { CardPaieService, DepartementServiceService } from '../../shared/services/catalog.service';
import { ClosingService } from '../../shared/services/closing.service';
import { ExcelExportService } from '../../shared/services/excel-export.service';
import { PrintReportService, PrintSession } from '../../shared/services/print-report.service';
import { ClosingPage } from './closing';
import { printFileName, printHeaderLines } from './closing.logic';

/** Lot D : rapport de contrôle imprimable (service d'impression partagé, liste complète, en-tête daté). */

const anomalie = (i: number): ClosingAnomalyView => ({
  type: 'PointageManquant',
  gravite: 'Critique',
  poids: 5,
  matricule: String(100 + i),
  nom: null,
  prenom: null,
  departement: 'D1',
  date: '2025-11-03T00:00:00',
  detail: `ligne ${i}`,
  heures: null,
});

const TOUTES = Array.from({ length: 250 }, (_, i) => anomalie(i));

const rapport = (page: number, pageSize: number, total = TOUTES.length): ClosingReport => ({
  from: '2025-11-01T00:00:00',
  to: '2025-11-30T00:00:00',
  departement: null,
  statut: 'ACorriger',
  statutLibelle: 'À corriger',
  seuilBloquant: 0.05,
  score: 97,
  formule: 'formule',
  joursEmployeControles: 100,
  nombreAnomalies: total,
  parGravite: [{ cle: 'Critique', nombre: total }],
  parType: [{ cle: 'PointageManquant', nombre: total }],
  anomalies: {
    items: TOUTES.slice((page - 1) * pageSize, page * pageSize),
    total,
    page,
    pageSize,
    totalPages: Math.ceil(TOUTES.length / pageSize),
  },
  parDepartement: [],
  comparaison: null,
  nomsAffiches: false,
});

describe('rapport imprimé : logique pure', () => {
  const filter = { from: '2025-11-01', to: '2025-11-30', departement: null, includeComparison: false };

  it('nom de fichier distinct de l’export Excel', () => {
    expect(printFileName(filter)).toBe('rapport-controle-avant-paie_2025-11-01_2025-11-30');
  });

  it('en-tête : période, résultat, liste imprimée, dates complètes (avec l’année), sans nom ni matricule', () => {
    const lignes = printHeaderLines(
      rapport(1, 50),
      filter,
      new Date(2025, 11, 1, 9, 5),
      { gravites: ['Critique'], tri: 'gravite', includeInformation: false, types: ['QuasiDoublon'] },
      new Date(2025, 11, 1, 9, 30),
    );
    const parLibelle = Object.fromEntries(lignes.map((l) => [l.label, l.value]));

    expect(parLibelle['Période']).toBe('du 01/11/2025 au 30/11/2025');
    expect(parLibelle['Département']).toBe('Tous');
    expect(parLibelle['Statut']).toBe('À corriger');
    expect(parLibelle['Score']).toBe('97 / 100');
    expect(parLibelle['Liste imprimée']).toBe('Filtre de gravité : Critique · Types : Quasi-doublons · tri par gravité');
    expect(parLibelle['Analyse du']).toBe('01/12/2025 à 09:05');
    expect(parLibelle['Rapport imprimé le']).toBe('01/12/2025 à 09:30');
  });
});

describe('page Contrôle avant paie : imprimer le rapport', () => {
  afterEach(() => TestBed.resetTestingModule());

  function creer(total?: number) {
    const requetes: ClosingQuery[] = [];
    const sessions: PrintSession[] = [];
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
        { provide: PrintReportService, useValue: { print: (s: PrintSession) => sessions.push(s) } },
        {
          provide: ClosingService,
          useValue: {
            checklist: () => of({ from: '', to: '', departement: null, lignes: [] }),
            analysis: (q: ClosingQuery) => {
              requetes.push(q);
              const r = rapport(q.page ?? 1, q.pageSize ?? 50);
              // Premier appel (analyse affichée) : total d'origine ; les suivants peuvent simuler un changement.
              return of(requetes.length > 1 && total !== undefined ? { ...r, anomalies: { ...r.anomalies, items: r.anomalies.items.slice(0, 10) } } : r);
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
    const bouton = () => Array.from(el.querySelectorAll('button')).find((b) => b.textContent?.includes('Imprimer le rapport'))!;
    return { fixture, el, requetes, sessions, bouton };
  }

  it('relit toutes les pages dans le cache, puis ouvre l’impression avec la liste complète et l’en-tête', () => {
    const { fixture, el, requetes, sessions, bouton } = creer();
    const avant = requetes.length;

    bouton().click();
    fixture.detectChanges();

    const lues = requetes.slice(avant);
    expect(lues.map((q) => q.page)).toEqual([1, 2]);
    expect(lues.every((q) => q.reuseCache === true && q.pageSize === 200)).toBe(true);
    expect(sessions).toHaveLength(1);
    // Nom de fichier = période ANALYSÉE (ici le mois précédent, période par défaut du filtre).
    expect(sessions[0].title).toBe(`rapport-controle-avant-paie_${lues[0].from}_${lues[0].to}`);

    sessions[0].onStart();
    fixture.detectChanges();
    expect(el.querySelectorAll('.closing-print__table tbody tr')).toHaveLength(250);
    expect(el.querySelector('.closing-print__head')?.textContent).toContain('Rapport de contrôle avant paie');
    expect(el.querySelector('data-table')?.classList.contains('print-hide')).toBe(true);
  });

  it('à la fermeture : écran normal, lignes d’impression libérées, bouton de nouveau actif', () => {
    const { fixture, el, sessions, bouton } = creer();
    bouton().click();
    fixture.detectChanges();
    sessions[0].onStart();

    sessions[0].onEnd();
    fixture.detectChanges();

    expect(el.querySelector('.closing-print__table')).toBeNull();
    expect(bouton().disabled).toBe(false);
  });

  it('données modifiées depuis l’analyse : message clair, aucune impression', () => {
    const { fixture, el, sessions } = creer(250);
    Array.from(el.querySelectorAll('button')).find((b) => b.textContent?.includes('Imprimer le rapport'))!.click();
    fixture.detectChanges();

    expect(sessions).toHaveLength(0);
    expect(el.querySelector('[role="alert"]')?.textContent).toContain('réanalysez avant d’imprimer le rapport');
  });

  it('sans résultat : le bouton est désactivé', () => {
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
        { provide: ClosingService, useValue: { analysis: vi.fn(), checklist: vi.fn() } },
      ],
    });
    const fixture = TestBed.createComponent(ClosingPage);
    fixture.detectChanges();
    const bouton = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button'))
      .find((b) => b.textContent?.includes('Imprimer le rapport'))!;
    expect(bouton.disabled).toBe(true);
  });
});
