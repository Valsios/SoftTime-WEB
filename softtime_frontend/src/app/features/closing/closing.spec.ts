import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { SessionStore } from '../../core/session.store';
import { ClosingAnomalyView, ClosingQuery, ClosingReport } from '../../shared/models';
import { CardPaieService, DepartementServiceService } from '../../shared/services/catalog.service';
import { ClosingService } from '../../shared/services/closing.service';
import { ClosingPage } from './closing';

const anomalie = (over: Partial<ClosingAnomalyView>): ClosingAnomalyView => ({
  type: 'PointageManquant',
  gravite: 'Critique',
  poids: 5,
  matricule: '777',
  nom: null,
  prenom: null,
  departement: 'D1',
  date: '2025-11-03T00:00:00',
  detail: 'x',
  heures: null,
  pointageExiste: null,
  ...over,
});

const rapport = (over: Partial<ClosingReport> = {}, items: ClosingAnomalyView[] = []): ClosingReport => ({
  from: '2025-11-01T00:00:00',
  to: '2025-11-30T00:00:00',
  departement: null,
  statut: 'ACorriger',
  statutLibelle: 'À corriger',
  seuilBloquant: 0.05,
  score: 97,
  formule: 'Score = formule',
  joursEmployeControles: 100,
  nombreAnomalies: items.length,
  parGravite: [
    { cle: 'Information', nombre: 2 },
    { cle: 'Mineure', nombre: 0 },
    { cle: 'Importante', nombre: 0 },
    { cle: 'Critique', nombre: 1 },
  ],
  parType: [],
  anomalies: { items, total: items.length, page: 1, pageSize: 50, totalPages: 1 },
  parDepartement: [],
  comparaison: null,
  nomsAffiches: true,
  salariesSansPlanningExclus: 0,
  ...over,
});

function creer(rapports: ClosingReport[]) {
  TestBed.resetTestingModule();
  const requetes: ClosingQuery[] = [];
  let appel = 0;
  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      { provide: SessionStore, useValue: { hasPermission: vi.fn(() => true) } },
      { provide: CardPaieService, useValue: { list: () => of([]) } },
      { provide: DepartementServiceService, useValue: { lookupBatch: () => of([]) } },
      {
        provide: ClosingService,
        useValue: {
          analysis: (q: ClosingQuery) => {
            requetes.push(q);
            return of(rapports[Math.min(appel++, rapports.length - 1)]);
          },
        },
      },
    ],
  });
  const fixture = TestBed.createComponent(ClosingPage);
  fixture.detectChanges();
  const el = fixture.nativeElement as HTMLElement;
  return { fixture, el, requetes };
}

describe('page Clôture de paie : niveau Information', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('affiche quatre cartes de gravité dont « Information » (badge neutre, avec texte)', () => {
    const { el } = creer([rapport()]);
    const cartes = Array.from(el.querySelectorAll('.closing-count'));

    expect(cartes).toHaveLength(4);
    const information = cartes[3] as HTMLElement;
    expect(information.textContent).toContain('Information');
    expect(information.textContent).toContain('2 informations sans effet sur le score');
    expect(information.querySelector('.soft-badge--muted')).not.toBeNull();
  });

  it('propose la case « Afficher aussi les informations » décochée, et ne demande pas les informations par défaut', () => {
    const { el, requetes } = creer([rapport()]);
    const case_ = Array.from(el.querySelectorAll<HTMLInputElement>('.closing-check input'))
      .find((i) => i.parentElement?.textContent?.includes('Afficher aussi les informations (sans effet sur le score)'));

    expect(case_).toBeDefined();
    expect(case_?.checked).toBe(false);
    expect(requetes[0].includeInformation).toBe(false);
  });

  it('cocher la case relance l’analyse avec includeInformation et revient à la page 1', () => {
    const { fixture, el, requetes } = creer([rapport(), rapport()]);
    const case_ = Array.from(el.querySelectorAll<HTMLInputElement>('.closing-check input'))[1];

    case_.checked = true;
    case_.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    expect(requetes).toHaveLength(2);
    expect(requetes[1].includeInformation).toBe(true);
    expect(requetes[1].page).toBe(1);
  });

  it('mentionne les salariés sans planning non contrôlés seulement quand il y en a', () => {
    const avec = creer([rapport({ salariesSansPlanningExclus: 22 })]);
    expect(avec.el.querySelector('.closing-excluded')?.textContent).toContain('22 salariés sans aucun planning sur la période ne sont pas contrôlés');

    const sans = creer([rapport({ salariesSansPlanningExclus: 0 })]);
    expect(sans.el.querySelector('.closing-excluded')).toBeNull();
  });

  it('l’explication dépliable lit le seuil réel et décrit le rôle de « Information »', () => {
    const { el } = creer([rapport({ seuilBloquant: 0.05 })]);
    const texte = el.querySelector('.closing-explain')?.textContent ?? '';

    expect(texte).toContain('Score = formule');
    expect(texte).toContain('5 %');
    expect(texte).toContain('Information');
    expect(texte).toContain('ni dans le statut');

    const autre = creer([rapport({ seuilBloquant: 0.1 })]);
    expect(autre.el.querySelector('.closing-explain')?.textContent).toContain('10 %');
  });

  it('férié non appliqué : lien « Corriger » seulement avec un pointage, sinon « Rien à corriger dans l’application »', () => {
    const items = [
      anomalie({ type: 'FerieNonApplique', gravite: 'Information', poids: 0, pointageExiste: true, date: '2025-11-01T00:00:00' }),
      anomalie({ type: 'FerieNonApplique', gravite: 'Information', poids: 0, pointageExiste: false, date: '2025-11-02T00:00:00' }),
    ];
    const { el } = creer([rapport({}, items)]);
    const lignes = Array.from(el.querySelectorAll('tbody tr')).filter((tr) => tr.textContent?.includes('Férié non appliqué'));

    expect(lignes).toHaveLength(2);
    expect(lignes[0].querySelector('a.closing-fix')).not.toBeNull();
    expect(lignes[0].textContent).not.toContain('Rien à corriger');
    expect(lignes[1].querySelector('a.closing-fix')).toBeNull();
    expect(lignes[1].textContent).toContain('Rien à corriger dans l’application');
  });

  it('liste vide parce que seules des informations existent : message calme, pas « aucune anomalie détectée »', () => {
    const { el } = creer([rapport({ nombreAnomalies: 2, parGravite: [{ cle: 'Information', nombre: 2 }] })]);
    const message = el.querySelector('.closing-state--success')?.textContent ?? '';

    expect(message).toContain('Aucune anomalie à corriger');
    expect(message).toContain('2 informations masquées');
    expect(message).not.toContain('Aucune anomalie détectée');
  });
});
