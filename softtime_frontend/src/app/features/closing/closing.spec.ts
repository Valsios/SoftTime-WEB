import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Observable, of, Subject } from 'rxjs';
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

  it('onglet et nouvelle analyse : jamais de réutilisation du cache', () => {
    const { fixture, el, requetes } = creer([avecInformations(), avecInformations(), avecInformations()]);

    el.querySelectorAll<HTMLElement>('[role="tab"]')[1].click();
    cliquerAnalyser(fixture, el);

    expect(requetes.map((r) => r.reuseCache)).toEqual([false, false, false]);
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
            analysis: (q: ClosingQuery) => {
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
          { provide: ClosingService, useValue: { analysis } },
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
