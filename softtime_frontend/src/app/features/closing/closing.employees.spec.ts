import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { SessionStore } from '../../core/session.store';
import { ClosingAnomalyView, ClosingEmployee, ClosingQuery, ClosingReport } from '../../shared/models';
import { CardPaieService, DepartementServiceService } from '../../shared/services/catalog.service';
import { ClosingService } from '../../shared/services/closing.service';
import { ExcelExportService } from '../../shared/services/excel-export.service';
import { ClosingPage } from './closing';
import { employeeRows, employeesCaption, exportSummary, listFilterText } from './closing.logic';

/** Lot B : salariés les plus touchés (B1) et filtre de la liste par salarié (B2). */

const salarie = (over: Partial<ClosingEmployee>): ClosingEmployee => ({
  matricule: '777',
  nom: 'DUPONT',
  prenom: 'Alix',
  departement: 'ATELIER',
  nombreAnomalies: 3,
  poids: 11,
  parGravite: [{ cle: 'Critique', nombre: 2 }, { cle: 'Mineure', nombre: 1 }],
  parType: [{ cle: 'PointageManquant', nombre: 2 }, { cle: 'QuasiDoublon', nombre: 1 }],
  ...over,
});

const anomalie = (matricule: string): ClosingAnomalyView => ({
  type: 'PointageManquant',
  gravite: 'Critique',
  poids: 5,
  matricule,
  nom: null,
  prenom: null,
  departement: 'ATELIER',
  date: '2025-11-03T00:00:00',
  detail: 'x',
  heures: null,
});

const rapport = (q: ClosingQuery): ClosingReport => {
  const items = [anomalie('777'), anomalie('888')].filter((a) => !q.matricule || a.matricule === q.matricule);
  return {
    from: '2025-11-01T00:00:00',
    to: '2025-11-30T00:00:00',
    departement: null,
    statut: 'ACorriger',
    statutLibelle: 'À corriger',
    seuilBloquant: 0.05,
    score: 97,
    formule: 'formule',
    joursEmployeControles: 100,
    nombreAnomalies: 2,
    parGravite: [{ cle: 'Critique', nombre: 2 }],
    parType: [{ cle: 'PointageManquant', nombre: 2 }],
    anomalies: { items, total: items.length, page: 1, pageSize: 50, totalPages: 1 },
    parDepartement: [],
    comparaison: null,
    nomsAffiches: true,
    parSalarie: [salarie({}), salarie({ matricule: '888', nom: null, prenom: null, poids: 5, nombreAnomalies: 1, parGravite: [{ cle: 'Critique', nombre: 1 }], parType: [{ cle: 'PointageManquant', nombre: 1 }] })],
    salariesConcernes: 57,
  };
};

describe('salariés les plus touchés : logique pure', () => {
  it('lignes : nom ou matricule, critiques et types en clair, ordre du serveur conservé', () => {
    const rows = employeeRows(rapport({}));
    expect(rows).toEqual([
      { matricule: '777', label: 'DUPONT Alix', departement: 'ATELIER', anomalies: 3, critiques: 2, types: 'pointages manquants (2), quasi-doublon (1)' },
      { matricule: '888', label: '888', departement: 'ATELIER', anomalies: 1, critiques: 1, types: 'pointage manquant (1)' },
    ]);
    expect(employeeRows({ parSalarie: null })).toEqual([]);
    expect(employeeRows({})).toEqual([]);
  });

  it('légende : plafond affiché quand il y a plus de salariés que de lignes', () => {
    expect(employeesCaption(20, 57)).toBe('20 salariés les plus touchés sur 57');
    expect(employeesCaption(3, 3)).toBe('3 salariés concernés');
    expect(employeesCaption(1, undefined)).toBe('1 salarié concerné');
  });

  it('filtre salarié : nommé dans la ligne de filtre et dans la synthèse d’export', () => {
    expect(listFilterText([], [], 'DUPONT Alix')).toBe('Salarié : DUPONT Alix');
    const r = rapport({});
    const filter = { from: '2025-11-01', to: '2025-11-30', departement: null, includeComparison: false };
    const sheet = exportSummary(r, filter, new Date(2025, 11, 1), 1, {
      gravites: [], tri: 'date', includeInformation: false, salarie: { matricule: '777', label: 'DUPONT Alix' },
    });
    expect(sheet.rows).toContainEqual(['Filtre salarié', 'DUPONT Alix']);
    expect(exportSummary(r, filter, new Date(2025, 11, 1), 1).rows).toContainEqual(['Filtre salarié', 'Aucun (tous les salariés)']);
  });
});

describe('ClosingService : paramètre matricule', () => {
  afterEach(() => TestBed.resetTestingModule());

  function envoyer(query: Parameters<ClosingService['analysis']>[0]) {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    const http = TestBed.inject(HttpTestingController);
    TestBed.inject(ClosingService).analysis(query).subscribe();
    return http.expectOne((r) => r.url.endsWith('/api/v1/closing/analysis')).request.params;
  }

  it('absent, nul ou vide : non envoyé ; sinon envoyé nettoyé', () => {
    for (const matricule of [undefined, null, '  ']) {
      TestBed.resetTestingModule();
      expect(envoyer({ from: '2025-11-01', matricule }).has('matricule')).toBe(false);
    }
    TestBed.resetTestingModule();
    expect(envoyer({ from: '2025-11-01', matricule: ' 777 ' }).get('matricule')).toBe('777');
  });
});

describe('page Contrôle avant paie : salariés les plus touchés', () => {
  afterEach(() => TestBed.resetTestingModule());

  function creer(hasPermission = true) {
    const requetes: ClosingQuery[] = [];
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        {
          provide: SessionStore,
          useValue: {
            hasPermission: vi.fn(() => hasPermission),
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
              return of(rapport(q));
            },
          },
        },
      ],
    });
    const fixture = TestBed.createComponent(ClosingPage);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const analyser = () => {
      Array.from(el.querySelectorAll('button')).find((b) => b.textContent?.trim() === 'Analyser')!.click();
      fixture.detectChanges();
    };
    analyser();
    const voir = (i: number) => Array.from(el.querySelectorAll<HTMLButtonElement>('button')).filter((b) => b.getAttribute('aria-label')?.startsWith('Voir les anomalies de'))[i];
    return { fixture, el, requetes, voir, analyser };
  }

  it('une carte avec un salarié par ligne, la légende du plafond et un bouton nommé', () => {
    const { el, voir } = creer();
    expect(el.textContent).toContain('Salariés les plus touchés');
    expect(el.textContent).toContain('2 salariés les plus touchés sur 57');
    expect(voir(0).getAttribute('aria-label')).toBe('Voir les anomalies de DUPONT Alix');
    expect(voir(1).getAttribute('aria-label')).toBe('Voir les anomalies de 888');
  });

  it('« Voir ses anomalies » : liste filtrée (page 1, cache), score inchangé, ligne « Salarié : … », lien Corriger prérempli', () => {
    const { fixture, el, requetes, voir } = creer();
    const avant = requetes.length;

    voir(0).click();
    fixture.detectChanges();

    expect(requetes).toHaveLength(avant + 1);
    expect(requetes[avant]).toMatchObject({ matricule: '777', page: 1, reuseCache: true });
    expect(el.querySelector('.closing-filter-line')?.textContent).toContain('Salarié : DUPONT Alix');
    expect(voir(0).getAttribute('aria-pressed')).toBe('true');
    const corriger = el.querySelector<HTMLAnchorElement>('a.closing-fix')!;
    expect(corriger.getAttribute('href')).toContain('matricule=777');
    expect(corriger.getAttribute('href')).toContain('/corrected-hours');
  });

  it('second clic ou « Tout afficher » : filtre retiré ; nouvelle analyse : filtre salarié oublié', () => {
    const { fixture, el, requetes, voir, analyser } = creer();
    voir(0).click();
    fixture.detectChanges();
    voir(0).click();
    fixture.detectChanges();
    expect(requetes[requetes.length - 1].matricule ?? null).toBeNull();

    voir(1).click();
    fixture.detectChanges();
    Array.from(el.querySelectorAll('button')).find((b) => b.textContent?.trim() === 'Tout afficher')!.click();
    fixture.detectChanges();
    expect(requetes[requetes.length - 1].matricule ?? null).toBeNull();

    voir(1).click();
    fixture.detectChanges();
    analyser();
    expect(requetes[requetes.length - 1]).toMatchObject({ reuseCache: false });
    expect(requetes[requetes.length - 1].matricule ?? null).toBeNull();
  });

  it('sans le droit de l’écran de correction : la mention « Accès requis » remplace le lien', () => {
    const { fixture, el, voir } = creer(false);
    voir(0).click();
    fixture.detectChanges();
    expect(el.querySelector('a.closing-fix')).toBeNull();
    expect(el.textContent).toContain('Accès « Heures corrigées » requis');
  });
});
