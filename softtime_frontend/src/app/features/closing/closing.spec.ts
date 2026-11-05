import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Observable, of, Subject } from 'rxjs';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { SessionStore } from '../../core/session.store';
import { ClosingAnomalyView, ClosingChecklist, ClosingQuery, ClosingReport } from '../../shared/models';
import { CardPaieService, DepartementServiceService } from '../../shared/services/catalog.service';
import { ClosingService } from '../../shared/services/closing.service';
import { ExcelExportService } from '../../shared/services/excel-export.service';
import { ClosingPage } from './closing';

/** Export Excel simulé : aucun téléchargement réel, seulement les appels reçus. */
const excelFactice = { downloadWorkbook: vi.fn() };

/** Checklist simulée par défaut dans les mocks du service (les tests de la carte la remplacent). */
const checklistFactice = (): Observable<ClosingChecklist> =>
  of({ from: '2025-11-01T00:00:00', to: '2025-11-30T00:00:00', departement: null, lignes: [] });

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

/** Session simulée : les signaux que lit l'état conservé (jeton, compte, bases actives). */
function fausseSession() {
  return {
    hasPermission: vi.fn(() => true),
    token: signal<string | null>('jeton'),
    session: signal<{ userId: number } | null>({ userId: 1 }),
    activeSageDb: signal<string | null>('SAGE_A'),
    activePointeuseDb: signal<string | null>('PTE_A'),
  };
}

function configurer(rapports: ClosingReport[]) {
  TestBed.resetTestingModule();
  const requetes: ClosingQuery[] = [];
  const session = fausseSession();
  let appel = 0;
  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      { provide: SessionStore, useValue: session },
      { provide: CardPaieService, useValue: { list: () => of([]) } },
      { provide: DepartementServiceService, useValue: { lookupBatch: () => of([]) } },
      { provide: ExcelExportService, useValue: excelFactice },
      {
        provide: ClosingService,
        useValue: {
          checklist: checklistFactice, analysis: (q: ClosingQuery) => {
            requetes.push(q);
            return of(rapports[Math.min(appel++, rapports.length - 1)]);
          },
        },
      },
    ],
  });
  return { requetes, session };
}

function monter() {
  const fixture = TestBed.createComponent(ClosingPage);
  fixture.detectChanges();
  return { fixture, el: fixture.nativeElement as HTMLElement };
}

function cliquerAnalyser(fixture: { detectChanges(): void }, el: HTMLElement) {
  Array.from(el.querySelectorAll('button')).find((b) => b.textContent?.trim() === 'Analyser')!.click();
  fixture.detectChanges();
}

/** Monte la page puis clique une fois sur « Analyser » : plus aucune analyse ne se lance à l'ouverture. */
function creer(rapports: ClosingReport[]) {
  const { requetes, session } = configurer(rapports);
  const { fixture, el } = monter();
  cliquerAnalyser(fixture, el);
  return { fixture, el, requetes, session };
}

describe('page Contrôle avant paie : niveau Information', () => {
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

  it('interrupteur de comparaison : nommé, état en texte, désactivé par défaut', () => {
    const { el, requetes } = creer([rapport()]);
    const interrupteur = el.querySelector<HTMLInputElement>('label.closing-switch input[type="checkbox"]');

    expect(interrupteur).not.toBeNull();
    expect(interrupteur?.checked).toBe(false);
    expect(el.querySelector('label.closing-switch')?.textContent).toContain('Comparer avec la période précédente');
    expect(el.querySelector('.closing-switch__state')?.textContent?.trim()).toBe('Désactivée');
    expect(requetes[0].includeComparison).toBe(false);
  });

  it('interrupteur de comparaison : au clavier ou à la souris, change l’état affiché sans relancer ; l’analyse suivante l’envoie', () => {
    const { fixture, el, requetes } = creer([rapport(), rapport()]);
    const interrupteur = el.querySelector<HTMLInputElement>('label.closing-switch input[type="checkbox"]')!;

    interrupteur.focus(); // un contrôle natif : Espace l’active comme un clic
    interrupteur.click();
    fixture.detectChanges();

    expect(requetes).toHaveLength(1);
    expect(el.querySelector('.closing-switch__state')?.textContent?.trim()).toBe('Activée');
    expect(el.querySelector('.closing-stale')?.textContent).toContain('Filtre modifié, réanalyser');

    cliquerAnalyser(fixture, el);
    expect(requetes).toHaveLength(2);
    expect(requetes[1].includeComparison).toBe(true);
    expect(el.querySelector('.closing-stale')).toBeNull();
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

  it('onglet « Avec informations » : includeInformation = true, retour à la page 1, filtres et comparaison conservés', () => {
    const { fixture, el, requetes } = creer([avecInformations(), avecInformations(), avecInformations(), avecInformations()]);
    el.querySelector<HTMLInputElement>('label.closing-switch input')!.click();
    cliquerAnalyser(fixture, el);
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
    expect(requetes[1].from).toBe(requetes[0].from);

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

  it('les légendes masquées (position absolue, dont celle du tableau partagé) ont un ancêtre positionné : la page ne déborde pas du cadre', () => {
    const { el } = creer([avecInformations()]);
    const hote = el.querySelector('app-closing') ?? el;
    const captions = Array.from(el.querySelectorAll<HTMLElement>('caption'));

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

  it('tableau partagé : légende lisible par les lecteurs d’écran, sans recherche, pagination serveur à 50 par défaut', () => {
    const { el, requetes } = creer([avecInformations({ anomalies: { items: [anomalie({})], total: 120, page: 1, pageSize: 50, totalPages: 3 } })]);
    const legende = Array.from(el.querySelectorAll('caption')).map((c) => c.textContent?.trim());

    expect(legende).toContain('Anomalies de la période, page 1 sur 3');
    expect(el.querySelector('.soft-table-search')).toBeNull();
    expect(requetes[0].pageSize).toBe(50);
    expect(el.querySelector('.soft-pagination-info')?.textContent).toContain('sur 120 résultats');
    const tailles = Array.from(el.querySelectorAll('.soft-pagination-size option')).map((o) => o.textContent?.trim());
    expect(tailles).toEqual(['10', '25', '50', '100']);
  });

  it('tableau partagé : page suivante et changement de taille relancent l’analyse (taille : retour page 1)', () => {
    const rapp = avecInformations({ anomalies: { items: [anomalie({})], total: 120, page: 1, pageSize: 50, totalPages: 3 } });
    const { fixture, el, requetes } = creer([rapp, rapp, rapp]);

    el.querySelector<HTMLElement>('button[aria-label="Page suivante"]')!.click();
    fixture.detectChanges();
    expect(requetes[1].page).toBe(2);
    expect(requetes[0].reuseCache).toBe(false); // nouvelle analyse : jamais de réutilisation
    expect(requetes[1].reuseCache).toBe(true); // changement de page : l'analyse déjà calculée peut servir

    const select = el.querySelector<HTMLSelectElement>('.soft-pagination-size select')!;
    select.value = '25';
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    expect(requetes[2].pageSize).toBe(25);
    expect(requetes[2].page).toBe(1);
    expect(requetes[2].reuseCache).toBe(true);
    expect(requetes).toHaveLength(3); // une seule requête par action, aucun appel en double
  });

  it('onglet : une requête de liste avec le cache du serveur ; seule une nouvelle analyse recalcule', () => {
    const { fixture, el, requetes } = creer([avecInformations(), avecInformations(), avecInformations()]);

    el.querySelectorAll<HTMLElement>('[role="tab"]')[1].click();
    cliquerAnalyser(fixture, el);

    expect(requetes.map((r) => r.reuseCache)).toEqual([false, true, false]);
    expect(requetes[1]).toMatchObject({ includeInformation: true, page: 1 });
  });

  it('tableau partagé : badge de gravité et noms masqués (matricule seul) conservés', () => {
    const { el } = creer([rapport({ nomsAffiches: false }, [anomalie({ matricule: '777', nom: null, prenom: null })])]);
    const ligne = el.querySelector('tbody tr')!;

    expect(ligne.querySelector('.soft-badge')?.textContent).toContain('Critique');
    expect(ligne.textContent).toContain('Pointage manquant');
    expect(ligne.querySelectorAll('td')[3].textContent?.trim()).toBe('777');
    expect(el.textContent).toContain('Les noms ne sont pas affichés sur ce serveur');
  });
});

describe('page Contrôle avant paie : filtre et résultat conservés pendant la session', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('à l’ouverture : aucune analyse automatique, invitation à cliquer sur « Analyser »', () => {
    const { requetes } = configurer([rapport()]);
    const { el } = monter();

    expect(requetes).toHaveLength(0);
    expect(el.textContent).toContain('aucune analyse ne se lance toute seule');
    expect(el.querySelector('.closing-overview')).toBeNull();
  });

  it('nombre de requêtes : zéro à l’ouverture, une seule au clic sur « Analyser » (analyse complète, sans cache)', () => {
    const { requetes } = configurer([rapport()]);
    const { fixture, el } = monter();
    expect(requetes).toHaveLength(0);

    cliquerAnalyser(fixture, el);

    expect(requetes).toHaveLength(1);
    expect(requetes[0].reuseCache).toBe(false);
    expect(requetes[0].page).toBe(1);
  });

  it('changement de page : une requête de page (cache autorisé), jamais « Analyse en cours… » ni zone des résultats occupée', () => {
    const flux: Subject<ClosingReport>[] = [];
    const requetes: ClosingQuery[] = [];
    const premiere = rapport({ anomalies: { items: [anomalie({})], total: 120, page: 1, pageSize: 50, totalPages: 3 } }, [anomalie({})]);
    let appel = 0;
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: SessionStore, useValue: fausseSession() },
        { provide: CardPaieService, useValue: { list: () => of([]) } },
        { provide: DepartementServiceService, useValue: { lookupBatch: () => of([]) } },
        {
          provide: ClosingService,
          useValue: {
            checklist: checklistFactice, analysis: (q: ClosingQuery) => {
              requetes.push(q);
              if (appel++ === 0) return of(premiere);
              const f = new Subject<ClosingReport>();
              flux.push(f);
              return f.asObservable();
            },
          },
        },
      ],
    });
    const { fixture, el } = monter();
    cliquerAnalyser(fixture, el);
    expect(requetes).toHaveLength(1);

    el.querySelector<HTMLElement>('button[aria-label="Page suivante"]')!.click();
    fixture.detectChanges();

    expect(requetes).toHaveLength(2); // une requête de plus, pas davantage
    expect(requetes[1].page).toBe(2);
    expect(requetes[1].reuseCache).toBe(true); // l’analyse déjà calculée peut servir : pas de nouveau calcul complet
    expect(el.querySelector('.closing-busy')).toBeNull(); // pas d’« Analyse en cours… » pour une page
    expect(el.querySelector('.closing-results')?.getAttribute('aria-busy')).toBe('false');
    expect(el.querySelector('.closing-overview')).not.toBeNull(); // la page ne se recharge pas
    expect(Array.from(el.querySelectorAll('button')).find((b) => b.textContent?.trim() === 'Analyser')!.disabled).toBe(false);
    flux[0].complete();
  });

  it('bandeau du résultat : date et heure, période et département', () => {
    const { el } = creer([rapport()]);
    const bandeau = el.querySelector('.closing-status-line')?.textContent ?? '';

    expect(bandeau).toMatch(/Résultat du \d{2}\/\d{2} à \d{2}:\d{2}, période du \d{2}\/\d{2}\/\d{4} au \d{2}\/\d{2}\/\d{4}, département tous\./);
  });

  it('quitter puis revenir : même résultat, même bandeau, aucune requête de plus', () => {
    const { fixture, el, requetes } = creer([rapport({}, [anomalie({})])]);
    const bandeau = el.querySelector('.closing-status-line')?.textContent;
    const score = el.querySelector('.closing-score__value')?.textContent;
    fixture.destroy();

    const retour = monter();

    expect(requetes).toHaveLength(1);
    expect(retour.el.querySelector('.closing-score__value')?.textContent).toBe(score);
    expect(retour.el.querySelector('.closing-status-line')?.textContent).toBe(bandeau);
    expect(retour.el.querySelectorAll('tbody tr')).toHaveLength(1);
  });

  it('filtre modifié : « filtre modifié, réanalyser », aucune requête ; revenir à l’ancien filtre efface le message', async () => {
    const { fixture, el, requetes } = creer([rapport()]);
    await fixture.whenStable(); // le modèle des champs est écrit après le premier rendu
    const debut = el.querySelector<HTMLInputElement>('input[name="from"]')!;
    const ancien = debut.value;

    debut.value = '2025-10-01';
    debut.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();
    expect(el.querySelector('.closing-stale')?.textContent).toContain('Filtre modifié, réanalyser');
    expect(requetes).toHaveLength(1);

    debut.value = ancien;
    debut.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();
    expect(el.querySelector('.closing-stale')).toBeNull();
  });

  it('un filtre saisi mais non analysé est conservé au retour sur la page', async () => {
    const { fixture, el, requetes } = creer([rapport()]);
    const debut = el.querySelector<HTMLInputElement>('input[name="from"]')!;
    debut.value = '2025-10-01';
    debut.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.destroy();

    const retour = monter();
    await retour.fixture.whenStable(); // le modèle des champs est écrit après le premier rendu
    retour.fixture.detectChanges();

    expect(retour.el.querySelector<HTMLInputElement>('input[name="from"]')?.value).toBe('2025-10-01');
    expect(requetes).toHaveLength(1);
  });

  it('déconnexion : le résultat conservé est vidé', () => {
    const { fixture, session } = creer([rapport()]);
    fixture.destroy();

    session.token.set(null);
    TestBed.tick(); // les effets s'exécutent à chaque cycle de l'application
    session.token.set('autre');
    const retour = monter();

    expect(retour.el.querySelector('.closing-overview')).toBeNull();
    expect(retour.el.textContent).toContain('aucune analyse ne se lance toute seule');
  });

  it('changement de base active (SAGE ou pointage) ou de compte : le résultat conservé est vidé', () => {
    const changements: ((s: ReturnType<typeof fausseSession>) => void)[] = [
      (s) => s.activeSageDb.set('SAGE_B'),
      (s) => s.activePointeuseDb.set('PTE_B'),
      (s) => s.session.set({ userId: 2 }),
    ];
    for (const changer of changements) {
      const { fixture, session } = creer([rapport()]);
      fixture.destroy();
      changer(session);

      const retour = monter();
      expect(retour.el.querySelector('.closing-overview')).toBeNull();
    }
  });

  describe('analyse en cours', () => {
    function configurerAvec(analysis: () => Observable<ClosingReport>) {
      TestBed.resetTestingModule();
      TestBed.configureTestingModule({
        providers: [
          provideRouter([]),
          { provide: SessionStore, useValue: fausseSession() },
          { provide: CardPaieService, useValue: { list: () => of([]) } },
          { provide: DepartementServiceService, useValue: { lookupBatch: () => of([]) } },
          { provide: ExcelExportService, useValue: excelFactice },
          { provide: ClosingService, useValue: { checklist: checklistFactice, analysis } },
        ],
      });
      return monter();
    }

    function enAttente() {
      const flux: Subject<ClosingReport>[] = [];
      let desabonnements = 0;
      const { fixture, el } = configurerAvec(
        () =>
          new Observable<ClosingReport>((abonne) => {
            const f = new Subject<ClosingReport>();
            flux.push(f);
            const sub = f.subscribe(abonne);
            return () => {
              desabonnements++;
              sub.unsubscribe();
            };
          }),
      );
      return { fixture, el, flux, desabonnements: () => desabonnements };
    }

    it('« Analyse en cours… » annoncé, zone des résultats marquée occupée, puis retirée à la fin', () => {
      const { fixture, el, flux } = enAttente();
      cliquerAnalyser(fixture, el);

      expect(el.querySelector('.closing-busy')?.textContent).toContain('Analyse en cours…');
      expect(el.querySelector('.closing-status-line')?.getAttribute('aria-live')).toBe('polite');

      flux[0].next(rapport());
      flux[0].complete();
      fixture.detectChanges();
      expect(el.querySelector('.closing-busy')).toBeNull();
      expect(el.querySelector('.closing-results')?.getAttribute('aria-busy')).toBe('false');
    });

    it('clics répétés sur le même filtre : une seule requête', () => {
      const { fixture, el, flux } = enAttente();
      cliquerAnalyser(fixture, el);
      cliquerAnalyser(fixture, el);
      cliquerAnalyser(fixture, el);

      expect(flux).toHaveLength(1);
    });

    it('bouton désactivé pendant l’analyse ; un autre filtre soumis quand même annule la requête précédente', () => {
      const { fixture, el, flux, desabonnements } = enAttente();
      cliquerAnalyser(fixture, el);
      const bouton = Array.from(el.querySelectorAll('button')).find((b) => b.textContent?.trim() === 'Analyser')!;
      expect(bouton.disabled).toBe(true);

      fixture.componentInstance.onSearch({ from: '2025-10-01', to: '2025-10-31', matriculeFrom: null, matriculeTo: null, departement: null });

      expect(flux).toHaveLength(2);
      expect(desabonnements()).toBe(1);
    });

    it('échec : message clair, dernier bon résultat gardé, indicateur retiré', () => {
      let appel = 0;
      const { fixture, el } = configurerAvec(() =>
        appel++ === 0
          ? of(rapport())
          : new Observable<ClosingReport>((o) => o.error({ status: 500, error: { error: 'Indisponible' } })),
      );
      cliquerAnalyser(fixture, el);
      cliquerAnalyser(fixture, el);

      expect(el.querySelector('.closing-alert--error')).not.toBeNull();
      expect(el.querySelector('.closing-busy')).toBeNull();
      expect(el.querySelector('.closing-overview')).not.toBeNull();
    });
  });
});

describe('page Contrôle avant paie : export Excel de la liste', () => {
  afterEach(() => {
    TestBed.resetTestingModule();
    excelFactice.downloadWorkbook.mockClear();
  });

  const TOTAL = 450;
  /** Page k d'une liste fictive de 450 lignes (détails « ligne 1 » à « ligne 450 »), 200 par page au plus. */
  const page = (k: number, taille: number): ClosingReport => {
    const debut = (k - 1) * taille;
    const items = Array.from({ length: Math.max(0, Math.min(taille, TOTAL - debut)) }, (_, i) =>
      anomalie({ detail: `ligne ${debut + i + 1}`, matricule: `M${debut + i + 1}` }),
    );
    return rapport(
      { nombreAnomalies: TOTAL, anomalies: { items, total: TOTAL, page: k, pageSize: taille, totalPages: Math.ceil(TOTAL / taille) } },
      items,
    );
  };

  function monterAvec(reponse: (q: ClosingQuery) => Observable<ClosingReport>) {
    TestBed.resetTestingModule();
    const requetes: ClosingQuery[] = [];
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: SessionStore, useValue: fausseSession() },
        { provide: CardPaieService, useValue: { list: () => of([]) } },
        { provide: DepartementServiceService, useValue: { lookupBatch: () => of([]) } },
        { provide: ExcelExportService, useValue: excelFactice },
        {
          provide: ClosingService,
          useValue: {
            checklist: checklistFactice, analysis: (q: ClosingQuery) => {
              requetes.push(q);
              return reponse(q);
            },
          },
        },
      ],
    });
    const { fixture, el } = monter();
    return { fixture, el, requetes };
  }

  const boutonExport = (el: HTMLElement) =>
    Array.from(el.querySelectorAll('button')).find((b) => b.textContent?.trim() === 'Exporter') as HTMLButtonElement;

  it('« Exporter » est désactivé tant qu’il n’y a pas de résultat, actif ensuite', () => {
    const { fixture, el } = monterAvec((q) => of(page(q.page ?? 1, q.pageSize ?? 50)));
    expect(boutonExport(el).disabled).toBe(true);

    cliquerAnalyser(fixture, el);
    expect(boutonExport(el).disabled).toBe(false);
  });

  it('« Exporter » est désactivé pendant l’analyse', () => {
    const flux = new Subject<ClosingReport>();
    const { fixture, el } = monterAvec(() => flux);
    cliquerAnalyser(fixture, el);

    expect(boutonExport(el).disabled).toBe(true);
    flux.next(page(1, 50));
    flux.complete();
    fixture.detectChanges();
    expect(boutonExport(el).disabled).toBe(false);
  });

  it('exporte TOUTE la liste (450 lignes, 3 pages de 200 au plus), même nombre et même ordre, sans relancer d’analyse', () => {
    const { fixture, el, requetes } = monterAvec((q) => of(page(q.page ?? 1, q.pageSize ?? 50)));
    cliquerAnalyser(fixture, el);
    const avant = requetes.length;

    boutonExport(el).click();
    fixture.detectChanges();

    const export_ = requetes.slice(avant);
    expect(export_.map((r) => r.page)).toEqual([1, 2, 3]);
    expect(export_.every((r) => r.reuseCache === true && r.pageSize === 200 && r.includeInformation === false)).toBe(true);   // suit l’onglet affiché
    expect(export_.every((r) => r.from === requetes[0].from && r.to === requetes[0].to)).toBe(true);

    expect(excelFactice.downloadWorkbook).toHaveBeenCalledTimes(1);
    const [nom, feuilles] = excelFactice.downloadWorkbook.mock.calls[0];
    expect(nom).toMatch(/^controle-avant-paie_\d{4}-\d{2}-\d{2}_\d{4}-\d{2}-\d{2}$/);
    const liste = feuilles.find((f: { sheetName: string }) => f.sheetName === 'Anomalies');
    expect(liste.rows).toHaveLength(TOTAL);
    expect(liste.rows.map((l: unknown[]) => l[6])).toEqual(Array.from({ length: TOTAL }, (_, i) => `ligne ${i + 1}`));
    expect(feuilles.map((f: { sheetName: string }) => f.sheetName)).toEqual(['Synthèse', 'Anomalies']);
  });

  it('clics répétés pendant la préparation : un seul export', () => {
    const flux: Subject<ClosingReport>[] = [];
    let appel = 0;
    const { fixture, el } = monterAvec((q) => {
      if (appel++ === 0) return of(page(1, q.pageSize ?? 50));
      const f = new Subject<ClosingReport>();
      flux.push(f);
      return f;
    });
    cliquerAnalyser(fixture, el);

    boutonExport(el).click();
    fixture.detectChanges();
    boutonExport(el).click();
    boutonExport(el).click();

    expect(flux).toHaveLength(1); // une seule lecture de liste en cours
    expect(boutonExport(el).disabled).toBe(true); // indicateur : bouton occupé
  });

  it('données modifiées depuis l’analyse : message clair, aucun fichier', () => {
    let appel = 0;
    const { fixture, el } = monterAvec((q) => {
      const p = page(q.page ?? 1, q.pageSize ?? 50);
      // Après l’analyse, la liste relue est plus courte que le résultat affiché.
      return of(appel++ === 0 ? p : { ...p, anomalies: { ...p.anomalies, items: [], totalPages: 1 } });
    });
    cliquerAnalyser(fixture, el);

    boutonExport(el).click();
    fixture.detectChanges();

    expect(excelFactice.downloadWorkbook).not.toHaveBeenCalled();
    expect(el.querySelector('.closing-alert--error')?.textContent).toContain('réanalysez avant d’exporter');
  });

  it('synthèse : période, département, statut, score et nombre de lignes exportées', () => {
    const { fixture, el } = monterAvec((q) => of(page(q.page ?? 1, q.pageSize ?? 50)));
    cliquerAnalyser(fixture, el);
    boutonExport(el).click();

    const synthese = excelFactice.downloadWorkbook.mock.calls[0][1][0];
    const valeur = (cle: string) => synthese.rows.find((l: unknown[]) => l[0] === cle)?.[1];
    expect(valeur('Période')).toMatch(/^du \d{2}\/\d{2}\/\d{4} au \d{2}\/\d{2}\/\d{4}$/);
    expect(valeur('Département')).toBe('Tous');
    expect(valeur('Statut')).toBe('À corriger');
    expect(valeur('Score')).toBe(97);
    expect(valeur('Lignes de la feuille « Anomalies »')).toBe(TOTAL);
  });
});

describe('page Contrôle avant paie : filtre et tri par gravité', () => {
  afterEach(() => {
    TestBed.resetTestingModule();
    excelFactice.downloadWorkbook.mockClear();
  });

  const CYCLE = ['Critique', 'Importante', 'Mineure', 'Information'] as const;
  const RANG: Record<string, number> = { Critique: 3, Importante: 2, Mineure: 1, Information: 0 };
  /** 40 lignes fictives « ligne 1 » à « ligne 40 », dont la gravité tourne dans l'ordre Critique, Importante, Mineure, Information. */
  const TOUTES = Array.from({ length: 40 }, (_, i) =>
    anomalie({ detail: `ligne ${i + 1}`, matricule: `M${i + 1}`, gravite: CYCLE[i % 4], poids: 1 }),
  );

  /** Serveur simulé : mêmes règles que le vrai (filtre explicite > onglet, tri stable) ; comptages toujours complets. */
  function serveur(q: ClosingQuery): ClosingReport {
    let liste = q.gravites?.length
      ? TOUTES.filter((a) => q.gravites!.includes(a.gravite))
      : q.includeInformation
        ? [...TOUTES]
        : TOUTES.filter((a) => a.gravite !== 'Information');
    if (q.tri === 'gravite') liste = [...liste].sort((a, b) => RANG[b.gravite] - RANG[a.gravite]);
    const taille = q.pageSize ?? 50;
    const page = q.page ?? 1;
    const items = liste.slice((page - 1) * taille, page * taille);
    return rapport(
      {
        nombreAnomalies: TOUTES.length,
        parGravite: CYCLE.map((cle) => ({ cle, nombre: 10 })),
        anomalies: { items, total: liste.length, page, pageSize: taille, totalPages: Math.max(1, Math.ceil(liste.length / taille)) },
      },
      items,
    );
  }

  function monterServeur() {
    const requetes: ClosingQuery[] = [];
    const session = fausseSession();
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: SessionStore, useValue: session },
        { provide: CardPaieService, useValue: { list: () => of([]) } },
        { provide: DepartementServiceService, useValue: { lookupBatch: () => of([]) } },
        { provide: ExcelExportService, useValue: excelFactice },
        {
          provide: ClosingService,
          useValue: {
            checklist: checklistFactice, analysis: (q: ClosingQuery) => {
              requetes.push(q);
              return of(serveur(q));
            },
          },
        },
      ],
    });
    const { fixture, el } = monter();
    cliquerAnalyser(fixture, el);
    return { fixture, el, requetes };
  }

  const carte = (el: HTMLElement, niveau: string) =>
    Array.from(el.querySelectorAll<HTMLButtonElement>('button.closing-count--toggle')).find((b) => b.textContent?.includes(niveau))!;
  const lignes = (el: HTMLElement) => Array.from(el.querySelectorAll('tbody tr')).map((tr) => tr.querySelectorAll('td')[6]?.textContent?.trim());

  it('sans filtre ni tri : la requête est identique à l’ancienne (aucun niveau, aucun tri)', () => {
    const { requetes } = monterServeur();

    expect(requetes).toHaveLength(1);
    expect(requetes[0].gravites ?? []).toEqual([]);
    expect(requetes[0].tri).toBeUndefined();
    expect(requetes[0].reuseCache).toBe(false);
  });

  it('une carte de gravité = une bascule : une seule requête de liste (page 1, cache), sans nouvelle analyse complète', () => {
    const { fixture, el, requetes } = monterServeur();

    carte(el, 'Critique').click();
    fixture.detectChanges();

    expect(requetes).toHaveLength(2);
    expect(requetes[1]).toMatchObject({ gravites: ['Critique'], page: 1, reuseCache: true });
    expect(carte(el, 'Critique').getAttribute('aria-pressed')).toBe('true');
    expect(carte(el, 'Mineure').getAttribute('aria-pressed')).toBe('false');
    // Les quatre comptages ne bougent pas : ils restent ceux de l’analyse complète.
    expect(Array.from(el.querySelectorAll('.closing-count strong')).map((n) => n.textContent)).toEqual(['10', '10', '10', '10']);
    expect(lignes(el).every((d) => d !== undefined)).toBe(true);
    expect(el.querySelector('.closing-busy')).toBeNull();
  });

  it('plusieurs niveaux (toujours du plus grave au moins grave), retrait d’un niveau et « Tout afficher »', () => {
    const { fixture, el, requetes } = monterServeur();

    carte(el, 'Mineure').click();
    carte(el, 'Critique').click();
    fixture.detectChanges();
    expect(requetes[requetes.length - 1].gravites).toEqual(['Critique', 'Mineure']);
    expect(el.querySelector('.closing-filter-line')?.textContent).toContain('Filtre de gravité : Critique, Mineure');
    expect(el.querySelector('[role="tab"]')).toBeNull();   // le filtre explicite remplace les onglets

    carte(el, 'Mineure').click();
    fixture.detectChanges();
    expect(requetes[requetes.length - 1].gravites).toEqual(['Critique']);

    el.querySelector<HTMLElement>('.closing-filter-line button')!.click();
    fixture.detectChanges();
    const dernier = requetes[requetes.length - 1];
    expect(dernier.gravites ?? []).toEqual([]);
    expect(dernier.reuseCache).toBe(true);
    expect(el.querySelector('[role="tab"]')).not.toBeNull();
    expect(requetes.every((r, i) => i === 0 || r.reuseCache === true)).toBe(true);   // jamais d’analyse complète relancée
  });

  it('tri par gravité : une requête de liste, du plus grave au moins grave, ordre habituel conservé à gravité égale', () => {
    const { fixture, el, requetes } = monterServeur();
    const select = el.querySelector<HTMLSelectElement>('select[name="tri"]')!;

    select.value = 'gravite';
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    expect(requetes).toHaveLength(2);
    expect(requetes[1]).toMatchObject({ tri: 'gravite', page: 1, reuseCache: true });
    const premieres = lignes(el).slice(0, 3);
    expect(premieres).toEqual(['ligne 1', 'ligne 5', 'ligne 9']);   // les « Critique », dans l’ordre d’origine
  });

  it('gravité, tri et onglet sans changement de période : aucune nouvelle analyse complète, ni checklist', () => {
    const { fixture, el, requetes } = monterServeur();
    const periode = (r: ClosingQuery) => [r.from, r.to, r.departement ?? null, r.includeComparison];

    carte(el, 'Critique').click();                                              // filtre de gravité
    fixture.detectChanges();
    el.querySelector<HTMLElement>('.closing-filter-line button')!.click();      // retour à « tout afficher »
    fixture.detectChanges();
    const select = el.querySelector<HTMLSelectElement>('select[name="tri"]')!;
    select.value = 'gravite';
    select.dispatchEvent(new Event('change'));                                  // tri
    fixture.detectChanges();
    el.querySelectorAll<HTMLElement>('[role="tab"]')[1].click();                // onglet « Avec informations »
    fixture.detectChanges();
    el.querySelectorAll<HTMLElement>('[role="tab"]')[0].click();                // retour à « À corriger »
    fixture.detectChanges();

    const apres = requetes.slice(1);                                            // la première est l'analyse du clic sur « Analyser »
    expect(apres).toHaveLength(5);                                              // une requête de liste par action
    expect(apres.every((r) => r.reuseCache === true)).toBe(true);               // jamais de nouveau calcul complet côté serveur
    expect(apres.every((r) => r.page === 1)).toBe(true);
    expect(apres.map(periode)).toEqual(apres.map(() => periode(requetes[0])));  // même période, même département
  });

  it('filtre et tri conservés en session : au retour sur l’écran, même liste, même état, aucune requête', () => {
    const { fixture, el, requetes } = monterServeur();
    carte(el, 'Importante').click();
    const select = el.querySelector<HTMLSelectElement>('select[name="tri"]')!;
    select.value = 'gravite';
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    const avant = requetes.length;
    fixture.destroy();

    const retour = monter();

    expect(requetes).toHaveLength(avant);
    expect(carte(retour.el, 'Importante').getAttribute('aria-pressed')).toBe('true');
    expect(retour.el.querySelector<HTMLSelectElement>('select[name="tri"]')!.selectedOptions[0].value).toBe('gravite');
    expect(retour.el.querySelector('.closing-filter-line')?.textContent).toContain('Importante');
  });

  it('filtre sans aucune ligne : message dédié, comptages inchangés', () => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: SessionStore, useValue: fausseSession() },
        { provide: CardPaieService, useValue: { list: () => of([]) } },
        { provide: DepartementServiceService, useValue: { lookupBatch: () => of([]) } },
        { provide: ExcelExportService, useValue: excelFactice },
        {
          provide: ClosingService,
          useValue: {
            checklist: checklistFactice, analysis: (q: ClosingQuery) =>
              of(q.gravites?.length ? rapport({ nombreAnomalies: 4, anomalies: { items: [], total: 0, page: 1, pageSize: 50, totalPages: 1 } }) : serveur(q)),
          },
        },
      ],
    });
    const { fixture, el } = monter();
    cliquerAnalyser(fixture, el);

    carte(el, 'Mineure').click();
    fixture.detectChanges();

    expect(el.querySelector('.closing-state')?.textContent).toContain('Aucune anomalie pour ce filtre de gravité');
    expect(el.querySelector('.closing-state')?.textContent).not.toContain('prêtes pour la clôture');
  });

  it('export : suit l’onglet, le filtre de gravité et le tri affichés ; la synthèse les indique', () => {
    const { fixture, el, requetes } = monterServeur();
    carte(el, 'Critique').click();
    carte(el, 'Information').click();
    const select = el.querySelector<HTMLSelectElement>('select[name="tri"]')!;
    select.value = 'gravite';
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    const avant = requetes.length;

    Array.from(el.querySelectorAll('button')).find((b) => b.textContent?.trim() === 'Exporter')!.click();
    fixture.detectChanges();

    const export_ = requetes.slice(avant);
    expect(export_).toHaveLength(1);   // 20 lignes filtrées tiennent sur une page de 200
    expect(export_[0]).toMatchObject({ gravites: ['Critique', 'Information'], tri: 'gravite', page: 1, pageSize: 200, reuseCache: true });
    expect(excelFactice.downloadWorkbook).toHaveBeenCalledTimes(1);
    const [, feuilles] = excelFactice.downloadWorkbook.mock.calls[0];
    const liste = feuilles.find((f: { sheetName: string }) => f.sheetName === 'Anomalies');
    expect(liste.rows).toHaveLength(20);   // même nombre que le total filtré affiché
    expect(liste.rows.slice(0, 3).map((l: unknown[]) => l[6])).toEqual(['ligne 1', 'ligne 5', 'ligne 9']);
    expect(liste.rows.slice(0, 10).every((l: unknown[]) => l[0] === 'Critique')).toBe(true);   // Critique d’abord
    expect(liste.rows.slice(10).every((l: unknown[]) => l[0] === 'Information')).toBe(true);
    const synthese = feuilles.find((f: { sheetName: string }) => f.sheetName === 'Synthèse');
    const valeur = (cle: string) => synthese.rows.find((l: unknown[]) => l[0] === cle)?.[1];
    expect(valeur('Filtre de gravité')).toBe('Critique, Information');
    expect(valeur('Tri de la liste')).toBe('Gravité décroissante, puis date');
    expect(valeur('Anomalies (toutes gravités)')).toBe(40);   // les comptages de la synthèse restent complets
  });

  it('export sans filtre ni tri : même liste que l’onglet affiché (hors « Information » par défaut)', () => {
    const { fixture, el, requetes } = monterServeur();
    const avant = requetes.length;

    Array.from(el.querySelectorAll('button')).find((b) => b.textContent?.trim() === 'Exporter')!.click();
    fixture.detectChanges();

    expect(requetes[avant]).toMatchObject({ includeInformation: false, reuseCache: true });
    expect(requetes[avant].gravites ?? []).toEqual([]);
    const liste = excelFactice.downloadWorkbook.mock.calls[0][1].find((f: { sheetName: string }) => f.sheetName === 'Anomalies');
    expect(liste.rows).toHaveLength(30);   // 40 lignes dont 10 « Information » masquées
  });
});

describe('page Contrôle avant paie : avancement de la paie', () => {
  afterEach(() => {
    TestBed.resetTestingModule();
    excelFactice.downloadWorkbook.mockClear();
  });

  const LIGNES: ClosingChecklist['lignes'] = [
    { code: 'Pointages', libelle: 'Pointages importés', etat: 'Fait', detail: '12 pointage(s) pour 3 salarié(s).', nombre: 12 },
    { code: 'Anomalies', libelle: 'Anomalies à arbitrer', etat: 'AFaire', detail: '2 anomalie(s) restent à arbitrer.', nombre: 2 },
    { code: 'Semaines', libelle: 'Semaines validées', etat: 'Information', detail: 'Aucune semaine planifiée à valider.', nombre: 0 },
    { code: 'ExoImpo', libelle: 'Calcul EXO/IMPO', etat: 'AFaire', detail: 'Aucune ligne EXO/IMPO pour cette période.', nombre: 0 },
    { code: 'SynchroPaie', libelle: 'Synchronisation paie', etat: 'NonDeterminable', detail: 'Aucune trace de synchronisation.', nombre: 0 },
  ];
  const liste = (): ClosingChecklist => ({ from: '2025-11-01T00:00:00', to: '2025-11-30T00:00:00', departement: null, lignes: LIGNES });

  function monterAvec(checklist: () => Observable<ClosingChecklist>, permissions = true) {
    TestBed.resetTestingModule();
    const analyses: ClosingQuery[] = [];
    const checklists: { from?: string | null; to?: string | null; departement?: string | null }[] = [];
    const session = { ...fausseSession(), hasPermission: vi.fn(() => permissions) };
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: SessionStore, useValue: session },
        { provide: CardPaieService, useValue: { list: () => of([]) } },
        { provide: DepartementServiceService, useValue: { lookupBatch: () => of([]) } },
        { provide: ExcelExportService, useValue: excelFactice },
        {
          provide: ClosingService,
          useValue: {
            analysis: (q: ClosingQuery) => {
              analyses.push(q);
              return of(rapport());
            },
            checklist: (q: { from?: string | null; to?: string | null; departement?: string | null }) => {
              checklists.push(q);
              return checklist();
            },
          },
        },
      ],
    });
    const { fixture, el } = monter();
    return { fixture, el, analyses, checklists, session };
  }

  it('aucune requête à l’ouverture : ni analyse ni checklist', () => {
    const { analyses, checklists, el } = monterAvec(() => of(liste()));

    expect(analyses).toHaveLength(0);
    expect(checklists).toHaveLength(0);
    expect(el.querySelector('.closing-checklist')).toBeNull();
  });

  it('au clic sur « Analyser » : une checklist, avec la même période et le même département que l’analyse', () => {
    const { fixture, el, analyses, checklists } = monterAvec(() => of(liste()));

    cliquerAnalyser(fixture, el);

    expect(analyses).toHaveLength(1);
    expect(checklists).toHaveLength(1);
    expect(checklists[0]).toMatchObject({ from: analyses[0].from, to: analyses[0].to, departement: analyses[0].departement ?? null });
  });

  it('une ligne par étape : état en texte, détail et lien vers l’écran existant', () => {
    const { fixture, el } = monterAvec(() => of(liste()));
    cliquerAnalyser(fixture, el);

    const lignes = Array.from(el.querySelectorAll('.closing-checklist li'));
    expect(lignes).toHaveLength(5);
    expect(lignes.map((l) => l.querySelector('.soft-badge')?.textContent?.trim())).toEqual(['Fait', 'À faire', 'Information', 'À faire', 'Non déterminable']);
    expect(lignes[1].textContent).toContain('2 anomalie(s) restent à arbitrer.');
    const liens = lignes.map((l) => l.querySelector('a')?.getAttribute('href'));
    expect(liens[0]).toContain('/punches');
    expect(liens[0]).toContain('du=2025-11-01');
    expect(liens[0]).toContain('au=2025-11-30');
    expect(liens[1]).toContain('/anomalies');
    expect(liens[2]).toBe('/weekly-validation');               // écran qui ne lit pas la période : aucun paramètre
    expect(liens[3]).toBe('/overtime');
    expect(el.querySelector('.closing-checklist')?.getAttribute('aria-label')).toBe('Avancement de la paie');
  });

  it('lien absent quand l’utilisateur n’a pas accès à l’écran (même règle que le menu)', () => {
    const { fixture, el } = monterAvec(() => of(liste()), false);
    cliquerAnalyser(fixture, el);

    expect(el.querySelectorAll('.closing-checklist li')).toHaveLength(5);
    expect(el.querySelector('.closing-checklist a')).toBeNull();
  });

  it('en parallèle et sans bloquer : la liste s’affiche même si la checklist tarde', () => {
    const attente = new Subject<ClosingChecklist>();
    const { fixture, el } = monterAvec(() => attente);

    cliquerAnalyser(fixture, el);

    expect(el.querySelector('.closing-overview')).not.toBeNull();   // l’analyse est affichée
    expect(el.textContent).toContain('Avancement de la paie en cours de chargement');
    attente.next(liste());
    attente.complete();
    fixture.detectChanges();
    expect(el.textContent).not.toContain('en cours de chargement');
    expect(el.querySelector('.closing-checklist')).not.toBeNull();
  });

  it('erreur de la checklist : simple note, l’analyse (score, statut, liste) reste intacte', () => {
    const { fixture, el } = monterAvec(() => new Observable<ClosingChecklist>((o) => o.error({ status: 500 })));

    cliquerAnalyser(fixture, el);

    expect(el.textContent).toContain('L’avancement de la paie n’a pas pu être chargé');
    expect(el.querySelector('.closing-alert--error')).toBeNull();
    expect(el.querySelector('.closing-score__value')?.textContent?.trim()).toBe('97 / 100');
    expect(el.querySelector('.closing-overview')).not.toBeNull();
  });

  it('conservée en session : au retour sur l’écran, même carte, aucune requête', () => {
    const { fixture, el, analyses, checklists } = monterAvec(() => of(liste()));
    cliquerAnalyser(fixture, el);
    fixture.destroy();

    const retour = monter();

    expect(analyses).toHaveLength(1);
    expect(checklists).toHaveLength(1);
    expect(retour.el.querySelectorAll('.closing-checklist li')).toHaveLength(5);
  });

  it('vidée au changement de base active', () => {
    const { fixture, el, session } = monterAvec(() => of(liste()));
    cliquerAnalyser(fixture, el);
    fixture.destroy();

    session.activeSageDb.set('SAGE_B');
    const retour = monter();

    expect(retour.el.querySelector('.closing-checklist')).toBeNull();
  });

  it('changement de page, filtre de gravité et tri : jamais de nouvelle checklist', () => {
    const { fixture, el, checklists } = monterAvec(() => of(liste()));
    cliquerAnalyser(fixture, el);

    el.querySelector<HTMLButtonElement>('button.closing-count--toggle')!.click();
    fixture.detectChanges();

    expect(checklists).toHaveLength(1);
  });
});

describe('page Contrôle avant paie : âge de l’analyse', () => {
  afterEach(() => {
    vi.useRealTimers();
    TestBed.resetTestingModule();
  });

  const statut = (el: HTMLElement) => el.querySelector('.closing-status-line')?.textContent?.replace(/\s+/g, ' ').trim() ?? '';

  it('texte seul, qui avance avec le temps sans aucune requête', () => {
    vi.useFakeTimers();
    const { fixture, el, requetes } = creer([rapport()]);
    expect(statut(el)).toContain('Analyse faite il y a moins d’une minute.');
    const avant = requetes.length;

    vi.advanceTimersByTime(3 * 60_000 + 5_000);
    fixture.detectChanges();
    expect(statut(el)).toContain('Analyse faite il y a 3 min.');

    vi.advanceTimersByTime(58 * 60_000);
    fixture.detectChanges();
    expect(statut(el)).toContain('Analyse faite il y a 1 h 1 min.');
    expect(requetes).toHaveLength(avant);                        // l’âge ne déclenche aucune requête
    expect(el.querySelector('.closing-age')?.querySelector('a, button')).toBeNull();
  });

  it('cohérent avec le résultat conservé : au retour sur l’écran, l’âge part de l’heure d’origine', () => {
    vi.useFakeTimers();
    const { fixture, requetes } = creer([rapport()]);
    fixture.destroy();
    vi.advanceTimersByTime(5 * 60_000);

    const retour = monter();

    expect(statut(retour.el)).toContain('Analyse faite il y a 5 min.');
    expect(requetes).toHaveLength(1);                            // retour sur l’écran : aucune requête
  });

  it('absent tant qu’il n’y a pas de résultat, remplacé par le message « filtre modifié »', () => {
    configurer([rapport()]);
    const { fixture, el } = monter();
    expect(statut(el)).not.toContain('Analyse faite');

    cliquerAnalyser(fixture, el);
    expect(statut(el)).toContain('Analyse faite');
    const interrupteur = el.querySelector<HTMLInputElement>('label.closing-switch input')!;
    interrupteur.click();
    fixture.detectChanges();
    expect(statut(el)).toContain('Filtre modifié, réanalyser');
    expect(statut(el)).not.toContain('Analyse faite');
  });
});
