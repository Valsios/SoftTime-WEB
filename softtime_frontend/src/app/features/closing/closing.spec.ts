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

  it('l’ancienne case « Afficher aussi les informations » et la case de comparaison ont disparu', () => {
    const { el } = creer([rapport()]);

    expect(el.querySelector('.closing-check')).toBeNull();
    expect(el.textContent).not.toContain('Afficher aussi les informations');
    expect(el.querySelectorAll('input[type="checkbox"]')).toHaveLength(1); // seul l’interrupteur de comparaison
  });

  it('interrupteur de comparaison : nommé, état en texte, désactivé par défaut', async () => {
    const { fixture, el, requetes } = creer([rapport()]);
    await fixture.whenStable();
    fixture.detectChanges();
    const interrupteur = el.querySelector<HTMLInputElement>('label.closing-switch input[type="checkbox"]');

    expect(interrupteur).not.toBeNull();
    expect(interrupteur?.checked).toBe(false);
    expect(el.querySelector('label.closing-switch')?.textContent).toContain('Comparer avec la période précédente');
    expect(el.querySelector('.closing-switch__state')?.textContent?.trim()).toBe('Désactivée');
    expect(requetes[0].includeComparison).toBe(false);
  });

  it('interrupteur de comparaison : au clavier ou à la souris, relance l’analyse et affiche « Activée »', async () => {
    const { fixture, el, requetes } = creer([rapport(), rapport()]);
    await fixture.whenStable();
    fixture.detectChanges();
    const interrupteur = el.querySelector<HTMLInputElement>('label.closing-switch input[type="checkbox"]')!;

    interrupteur.focus(); // un contrôle natif : Espace l’active comme un clic
    interrupteur.click();
    fixture.detectChanges();

    expect(requetes).toHaveLength(2);
    expect(requetes[1].includeComparison).toBe(true);
    expect(el.querySelector('.closing-switch__state')?.textContent?.trim()).toBe('Activée');

    interrupteur.click();
    fixture.detectChanges();
    expect(requetes[2].includeComparison).toBe(false);
    expect(el.querySelector('.closing-switch__state')?.textContent?.trim()).toBe('Désactivée');
  });

  const avecInformations = (over: Partial<ClosingReport> = {}) =>
    rapport(
      {
        nombreAnomalies: 5,
        parGravite: [
          { cle: 'Information', nombre: 2 },
          { cle: 'Critique', nombre: 3 },
        ],
        ...over,
      },
      [anomalie({})],
    );

  it('deux onglets avec leurs totaux : « À corriger (N) » et « Avec informations (M) »', () => {
    const { el } = creer([avecInformations()]);
    const onglets = Array.from(el.querySelectorAll<HTMLElement>('[role="tab"]'));

    expect(onglets.map((o) => o.textContent?.trim())).toEqual(['À corriger (3)', 'Avec informations (5)']);
    expect(onglets[0].getAttribute('aria-selected')).toBe('true');
    expect(onglets[1].getAttribute('aria-selected')).toBe('false');
  });

  it('onglet « Avec informations » : includeInformation = true, retour à la page 1, filtres et comparaison conservés', async () => {
    const { fixture, el, requetes } = creer([avecInformations(), avecInformations(), avecInformations()]);
    await fixture.whenStable();
    fixture.detectChanges();
    el.querySelector<HTMLInputElement>('label.closing-switch input')!.click();
    fixture.detectChanges();
    fixture.componentInstance.goTo(3);
    fixture.detectChanges();

    el.querySelectorAll<HTMLElement>('[role="tab"]')[1].click();
    fixture.detectChanges();
    const dernier = requetes[requetes.length - 1];

    expect(dernier.includeInformation).toBe(true);
    expect(dernier.page).toBe(1);
    expect(dernier.includeComparison).toBe(true);
    expect(dernier.from).toBe(requetes[0].from);
    expect(dernier.to).toBe(requetes[0].to);
    expect(requetes[0].includeInformation).toBe(false);

    el.querySelectorAll<HTMLElement>('[role="tab"]')[0].click();
    fixture.detectChanges();
    expect(requetes[requetes.length - 1].includeInformation).toBe(false);
  });

  it('onglets : lecture au clavier (flèche droite) et recliquer l’onglet actif ne relance rien', () => {
    const { fixture, el, requetes } = creer([avecInformations(), avecInformations()]);
    const onglets = el.querySelectorAll<HTMLElement>('[role="tab"]');

    onglets[0].click();
    expect(requetes).toHaveLength(1);

    onglets[0].dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true }));
    fixture.detectChanges();

    expect(requetes).toHaveLength(2);
    expect(requetes[1].includeInformation).toBe(true);
    expect(el.querySelectorAll<HTMLElement>('[role="tab"]')[1].getAttribute('aria-selected')).toBe('true');
  });

  it('les légendes masquées (position absolue) ont un ancêtre positionné : la page ne déborde pas du cadre', () => {
    const { el } = creer([avecInformations()]);
    const hote = el.querySelector('app-closing') ?? el;
    const captions = Array.from(el.querySelectorAll<HTMLElement>('caption.closing-sr'));

    expect(captions.length).toBeGreaterThan(0);
    // Sans ancêtre positionné, une légende absolue étendrait le document au-delà du cadre de l’application.
    let positionne = false;
    for (let n: HTMLElement | null = captions[0].parentElement; n && n !== document.body; n = n.parentElement) {
      if (getComputedStyle(n).position !== 'static') positionne = true;
    }
    expect(getComputedStyle(captions[0]).position).toBe('absolute');
    expect(positionne || getComputedStyle(hote).position === 'relative').toBe(true);
  });

  it('le score et le statut ne dépendent pas de l’onglet', () => {
    const { el } = creer([avecInformations(), avecInformations()]);
    const avant = el.querySelector('.closing-score__value')?.textContent;

    el.querySelectorAll<HTMLElement>('[role="tab"]')[1].click();

    expect(el.querySelector('.closing-score__value')?.textContent).toBe(avant);
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
