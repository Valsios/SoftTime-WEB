import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Observable, of } from 'rxjs';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { SessionStore } from '../../core/session.store';
import { ClosingChecklist, ClosingDepartment, ClosingQuery, ClosingReport } from '../../shared/models';
import { CardPaieService, DepartementServiceService } from '../../shared/services/catalog.service';
import { ClosingService } from '../../shared/services/closing.service';
import { ExcelExportService } from '../../shared/services/excel-export.service';
import { ClosingPage } from './closing';

/** Lot A2 : le tableau « Par département » est actionnable (préremplit le filtre et relance l'analyse). */

const checklistFactice = (): Observable<ClosingChecklist> =>
  of({ from: '2025-11-01T00:00:00', to: '2025-11-30T00:00:00', departement: null, lignes: [] });

const departement = (nom: string | null): ClosingDepartment => ({
  departement: nom,
  score: 90,
  joursEmployeControles: 10,
  nombreAnomalies: 2,
  parGravite: [{ cle: 'Critique', nombre: 1 }],
  parType: [],
});

const rapport = (over: Partial<ClosingReport> = {}): ClosingReport => ({
  from: '2025-11-01T00:00:00',
  to: '2025-11-30T00:00:00',
  departement: null,
  statut: 'ACorriger',
  statutLibelle: 'À corriger',
  seuilBloquant: 0.05,
  score: 97,
  formule: 'Score = formule',
  joursEmployeControles: 100,
  nombreAnomalies: 0,
  parGravite: [],
  parType: [],
  anomalies: { items: [], total: 0, page: 1, pageSize: 50, totalPages: 1 },
  parDepartement: [departement('ATELIER'), departement(null)],
  comparaison: null,
  nomsAffiches: true,
  ...over,
});

function creer(over: Partial<ClosingReport> = {}) {
  TestBed.resetTestingModule();
  const requetes: ClosingQuery[] = [];
  const checklists: { departement?: string | null }[] = [];
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
          checklist: (q: { departement?: string | null }) => {
            checklists.push(q);
            return checklistFactice();
          },
          analysis: (q: ClosingQuery) => {
            requetes.push(q);
            return of(rapport({ departement: q.departement ?? null, ...over }));
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
  return { fixture, el, requetes, checklists };
}

describe('page Contrôle avant paie : analyse par département depuis le tableau', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('un bouton par département nommé, aucun pour « Sans département »', () => {
    const { el } = creer();
    const boutons = Array.from(el.querySelectorAll('tbody button'));

    expect(boutons).toHaveLength(1);
    expect(boutons[0].getAttribute('aria-label')).toBe('Analyser le département ATELIER');
  });

  it('le clic relance une analyse complète du seul département, même période, avec sa checklist', () => {
    const { fixture, el, requetes, checklists } = creer();
    const avant = requetes.length;

    (el.querySelector('tbody button') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(requetes).toHaveLength(avant + 1);
    const derniere = requetes[requetes.length - 1];
    expect(derniere.departement).toBe('ATELIER');
    expect(derniere.reuseCache ?? false).toBe(false); // analyse complète, jamais le cache
    expect(derniere.from).toBe(requetes[avant - 1].from); // même période que le résultat affiché
    expect(derniere.to).toBe(requetes[avant - 1].to);
    expect(checklists[checklists.length - 1].departement).toBe('ATELIER');
  });

  it('puce de type sans ligne à corriger : le message renvoie vers l’onglet « Avec informations »', () => {
    // 537 quasi-doublons comptés, tous de niveau Information : la liste « À corriger » filtrée est vide.
    const { fixture, el, requetes } = creer({
      nombreAnomalies: 537,
      parType: [{ cle: 'QuasiDoublon', nombre: 537 }, { cle: 'PointageManquant', nombre: 0 }, { cle: 'DoublonExact', nombre: 2 }],
      parGravite: [{ cle: 'Information', nombre: 539 }],
    });
    const puces = Array.from(el.querySelectorAll<HTMLButtonElement>('.closing-chip'));
    expect(puces.map((p) => p.textContent?.replace(/\s+/g, ' ').trim())).toEqual(['Doublons exacts 2', 'Quasi-doublons 537']);

    puces[1].click();
    fixture.detectChanges();

    expect(requetes[requetes.length - 1]).toMatchObject({ types: ['QuasiDoublon'], reuseCache: true, page: 1 });
    expect(puces[1].getAttribute('aria-pressed')).toBe('true');
    expect(el.querySelector('.closing-state')?.textContent).toContain('ouvrez l’onglet « Avec informations »');
  });

  it('le bandeau du résultat nomme ensuite le département analysé', () => {
    const { fixture, el } = creer();
    (el.querySelector('tbody button') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(el.querySelector('.closing-muted')?.textContent).toContain('département ATELIER');
  });
});
