import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Observable, of, throwError } from 'rxjs';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { SessionStore } from '../../core/session.store';
import { PagedResult, ReportFilter, ReportRow } from '../../shared/models';
import { CardPaieService, DepartementServiceService } from '../../shared/services/catalog.service';
import { ExcelExportService } from '../../shared/services/excel-export.service';
import { ReportsService } from '../../shared/services/reports.service';
import { REPORTS, ReportsPage } from './reports';
import {
  ABSENCES_JUSTIFIEES,
  JUSTIFIEES_COLUMNS,
  JUSTIFIEES_EMPTY,
  JUSTIFIEES_NOTICE,
} from './reports.logic';

/** Lot J2 : type de rapport « Absences justifiées » (informatif, sans effet sur la paie). */

const filtre: ReportFilter = { from: '2025-11-03', to: '2025-11-07', departement: null };

const LIGNE = {
  matricule: '0123',
  nom: 'Martin',
  prenom: 'Paul',
  departement: 'RH',
  date: '2025-11-04T00:00:00',
  periode: 'Journée',
  dureeHeures: 8,
  type: 'Événement SAGE',
  motif: 'CONGE',
} as unknown as ReportRow;

const page = (items: ReportRow[]): PagedResult<ReportRow> =>
  ({ items, total: items.length, page: 1, pageSize: 50, totalPages: 1 }) as PagedResult<ReportRow>;

interface Doubles {
  paged: ReturnType<typeof vi.fn>;
  absences: ReturnType<typeof vi.fn>;
  absencesJustifiees: ReturnType<typeof vi.fn>;
  download: ReturnType<typeof vi.fn>;
}

function monter(paged: (type: string) => Observable<PagedResult<ReportRow>>, export_: ReportRow[] = [LIGNE]) {
  TestBed.resetTestingModule();
  const doubles: Doubles = {
    paged: vi.fn((type: string) => paged(type)),
    absences: vi.fn(() => of([])),
    absencesJustifiees: vi.fn(() => of(export_)),
    download: vi.fn(),
  };
  TestBed.configureTestingModule({
    providers: [
      { provide: ReportsService, useValue: doubles },
      {
        provide: ExcelExportService,
        useValue: { exporting: signal(false), track: (o: Observable<unknown>) => o, download: doubles.download },
      },
      { provide: CardPaieService, useValue: { list: () => of([]) } },
      { provide: DepartementServiceService, useValue: { lookupBatch: () => of([]) } },
      { provide: SessionStore, useValue: { hasRight: () => true, hasPermission: () => true } },
    ],
  });
  const fixture = TestBed.createComponent(ReportsPage);
  fixture.detectChanges();
  const ouvrir = (type: string) => {
    fixture.componentInstance.onTypeChange(type);
    fixture.detectChanges();
  };
  const chercher = () => {
    fixture.componentInstance.onSearch(filtre);
    fixture.detectChanges();
  };
  return { fixture, page: fixture.componentInstance, el: fixture.nativeElement as HTMLElement, doubles, ouvrir, chercher };
}

describe('Rapports : « Absences justifiées »', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('le type est dans la liste, juste après « État absences »', () => {
    const ids = REPORTS.map((r) => r.id);
    expect(REPORTS.find((r) => r.id === ABSENCES_JUSTIFIEES)?.label).toBe('Absences justifiées');
    expect(ids.indexOf(ABSENCES_JUSTIFIEES)).toBe(ids.indexOf('absences') + 1);
  });

  it('colonnes : Matricule, Nom, Prénom, Département, Date, Motif, Durée, dans cet ordre', () => {
    expect(JUSTIFIEES_COLUMNS.map((c) => c.label)).toEqual([
      'Matricule', 'Nom', 'Prénom', 'Département', 'Date', 'Motif', 'Durée',
    ]);
    expect(JUSTIFIEES_COLUMNS.map((c) => c.key)).toEqual([
      'matricule', 'nom', 'prenom', 'departement', 'date', 'motif', 'dureeHeures',
    ]);
  });

  it('affichage : date au format de l’application, durée en heures, valeurs vides sans « null »', () => {
    const [, nom, , , date, motif, duree] = JUSTIFIEES_COLUMNS;
    expect(date.format!('2025-11-04T00:00:00', {} as never)).toBe('2025-11-04');
    expect(duree.format!(8, {} as never)).toBe('8 h');
    expect(duree.format!(null, {} as never)).toBe('-');
    expect(nom.format!(null, {} as never)).toBe('');
    expect(motif.format!('CONGE + MISE A PIED', {} as never)).toBe('CONGE + MISE A PIED');
  });

  it('la mention « État informatif : sans effet sur la paie. » n’apparaît que pour ce type', () => {
    const { el, fixture, ouvrir } = monter(() => of(page([])));
    expect(el.textContent).not.toContain(JUSTIFIEES_NOTICE);

    ouvrir(ABSENCES_JUSTIFIEES);
    expect(el.querySelector('.reports-note')?.textContent?.trim()).toBe('État informatif : sans effet sur la paie.');

    ouvrir('absences');
    expect(el.querySelector('.reports-note')).toBeNull();
    fixture.destroy();
  });

  it('charge la version paginée du bon type et affiche les 7 colonnes (tableau et cartes)', () => {
    const { el, doubles, ouvrir, chercher } = monter(() => of(page([LIGNE])));
    ouvrir(ABSENCES_JUSTIFIEES);
    chercher();

    expect(doubles.paged).toHaveBeenCalledWith(ABSENCES_JUSTIFIEES, expect.objectContaining({ from: '2025-11-03', page: 1 }));
    const entetes = Array.from(el.querySelectorAll('thead th')).map((th) => th.textContent?.trim());
    expect(entetes).toEqual(['Matricule', 'Nom', 'Prénom', 'Département', 'Date', 'Motif', 'Durée']);
    const cellules = Array.from(el.querySelectorAll('tbody tr:first-child td')).map((td) => td.textContent?.trim());
    expect(cellules).toEqual(['0123', 'Martin', 'Paul', 'RH', '2025-11-04', 'CONGE', '8 h']);
    // Vue en cartes (sous 768 px) : mêmes libellés.
    const libellesCarte = Array.from(el.querySelectorAll('.soft-table-card:first-child .soft-table-card__field > span')).map(
      (s) => s.textContent?.trim(),
    );
    expect(libellesCarte).toEqual(['Matricule', 'Nom', 'Prénom', 'Département', 'Date', 'Motif', 'Durée']);
  });

  it('état vide : « Aucune absence justifiée sur cette période. » après une recherche, pas avant', () => {
    const { el, ouvrir, chercher } = monter(() => of(page([])));
    ouvrir(ABSENCES_JUSTIFIEES);
    expect(el.textContent).not.toContain(JUSTIFIEES_EMPTY);

    chercher();
    expect(el.textContent).toContain('Aucune absence justifiée sur cette période.');
  });

  it('erreur 400 (congés SAGE indisponibles) : message du serveur tel quel, jamais une liste vide muette', () => {
    const message = 'Les congés SAGE sont indisponibles : l’état des absences justifiées ne peut pas être calculé.';
    const { el, page: composant, ouvrir, chercher } = monter(() =>
      throwError(() => ({ status: 400, error: { error: message } })),
    );
    ouvrir(ABSENCES_JUSTIFIEES);
    chercher();

    expect(composant.erreur()).toBe(message);
    expect(el.querySelector('.reports-alert')?.textContent?.trim()).toBe(message);
    expect(el.textContent).not.toContain(JUSTIFIEES_EMPTY);
    expect(el.querySelector('data-table')).toBeNull();
  });

  it('l’erreur disparaît à la recherche suivante réussie', () => {
    let echec = true;
    const { el, page: composant, ouvrir, chercher } = monter(() =>
      echec ? throwError(() => ({ status: 400, error: { error: 'Indisponible.' } })) : of(page([LIGNE])),
    );
    ouvrir(ABSENCES_JUSTIFIEES);
    chercher();
    expect(composant.erreur()).toBe('Indisponible.');

    echec = false;
    chercher();
    expect(composant.erreur()).toBeNull();
    expect(el.querySelector('.reports-alert')).toBeNull();
    expect(el.querySelectorAll('tbody tr').length).toBe(1);
  });

  it('export Excel : mêmes sept colonnes et mêmes libellés que l’écran, via l’état non paginé', () => {
    const { doubles, ouvrir, chercher, page: composant } = monter(() => of(page([LIGNE])), [LIGNE]);
    ouvrir(ABSENCES_JUSTIFIEES);
    chercher();
    composant.exportExcel();

    expect(doubles.absencesJustifiees).toHaveBeenCalledWith(filtre);
    expect(doubles.download).toHaveBeenCalledTimes(1);
    const [nom, colonnes, lignes] = doubles.download.mock.calls[0];
    expect(nom).toBe(`rapport-${ABSENCES_JUSTIFIEES}`);
    expect(colonnes).toBe(JUSTIFIEES_COLUMNS);
    expect(colonnes.map((c: { label: string }) => c.label)).toEqual([
      'Matricule', 'Nom', 'Prénom', 'Département', 'Date', 'Motif', 'Durée',
    ]);
    expect(lignes).toEqual([LIGNE]);
  });
});

describe('Rapports : « État absences » inchangé', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('mêmes colonnes qu’avant (sans motif), ni mention informative, ni état vide propre', () => {
    const { el, doubles, ouvrir, chercher } = monter(() => of(page([LIGNE])));
    ouvrir('absences');
    chercher();

    expect(doubles.paged).toHaveBeenCalledWith('absences', expect.anything());
    const entetes = Array.from(el.querySelectorAll('thead th')).map((th) => th.textContent?.trim());
    expect(entetes).toEqual(['Matricule', 'Nom', 'Prénom', 'Département', 'Date', 'Durée (h)']);
    expect(el.querySelector('.reports-note')).toBeNull();
  });

  it('export « État absences » : colonnes d’export d’origine (durée brute, libellé « Durée (h) »)', () => {
    const { doubles, ouvrir, chercher, page: composant } = monter(() => of(page([LIGNE])));
    ouvrir('absences');
    chercher();
    composant.exportExcel();

    expect(doubles.absences).toHaveBeenCalledWith(filtre);
    const colonnes = doubles.download.mock.calls[0][1] as { label: string; key: string }[];
    expect(colonnes.map((c) => c.label)).toEqual(['Matricule', 'Nom', 'Prénom', 'Département', 'Date', 'Durée (h)']);
  });

  it('en cas d’erreur, le comportement d’avant est conservé (pas de message en ligne)', () => {
    const { el, page: composant, ouvrir, chercher } = monter(() => throwError(() => ({ status: 500 })));
    ouvrir('absences');
    chercher();

    expect(composant.erreur()).toBeNull();
    expect(el.querySelector('.reports-alert')).toBeNull();
    expect(el.querySelector('data-table')).not.toBeNull();
  });
});
