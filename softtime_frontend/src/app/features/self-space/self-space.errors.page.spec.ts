import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Observable, of, throwError } from 'rxjs';
import { afterEach, describe, expect, it } from 'vitest';
import { SelfSpacePage } from './self-space';
import { SelfAbsences, SelfAnomalies, SelfDelays, SelfSpaceService, SelfWeeks } from './self-space.service';

const periode = { from: '2026-11-01', to: '2026-11-06' };
const donnees = {
  weeks: { periode, semaines: [{ lundi: '2026-10-26T00:00:00', dimanche: '2026-11-01T00:00:00', heuresTravaillees: 45, heuresSupplementaires: 10, hs130: 8, hs150: 2, nuit: 0, dimanches: 0, feries: 0, retard: 0, absence: 0 }], exoImpo: [] } as SelfWeeks,
  anomalies: { periode, anomalies: [] } as SelfAnomalies,
  absences: { periode, absences: [] } as SelfAbsences,
  delays: { periode, retards: [] } as SelfDelays,
};

type Methode = 'weeks' | 'anomalies' | 'absences' | 'delays';

/** Les quatre lectures échouent toutes avec la même erreur (cas d'un refus de compte), sauf si `seulement` en désigne une. */
function monter(erreur: unknown, seulement?: Methode) {
  TestBed.resetTestingModule();
  const appels: string[] = [];
  const lecture = (m: Methode) => (): Observable<unknown> => {
    appels.push(m);
    return !seulement || seulement === m ? throwError(() => erreur) : of(donnees[m]);
  };
  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      { provide: SelfSpaceService, useValue: { weeks: lecture('weeks'), anomalies: lecture('anomalies'), absences: lecture('absences'), delays: lecture('delays') } },
    ],
  });
  const fixture = TestBed.createComponent(SelfSpacePage);
  fixture.detectChanges();
  const el = fixture.nativeElement as HTMLElement;
  return { fixture, el, appels, encadre: el.querySelector('.self-notice') as HTMLElement | null };
}

const refus = (message: string) => ({ status: 403, error: { error: message } });

describe('Mon espace : encadré durable des erreurs de compte (403)', () => {
  afterEach(() => TestBed.resetTestingModule());

  const cas: [string, string][] = [
    ['matricule inconnu', 'Ce matricule est inconnu dans la base : contactez la RH.'],
    ['plusieurs comptes', 'Ce matricule est porté par plusieurs comptes : contactez la RH.'],
    ['une seule base', 'Ce compte doit avoir exactement une base autorisée : contactez la RH.'],
    ['sans accès à Mon espace', 'Ce compte n\'a pas accès à Mon espace.'],
    ['sans matricule', 'Aucun matricule n\'est associé à ce compte : contactez la RH.'],
  ];

  for (const [nom, message] of cas) {
    it(`403 ${nom} : message du serveur et consigne « Contactez la RH. »`, () => {
      const { el, encadre } = monter(refus(message));

      expect(encadre).not.toBeNull();
      expect(encadre!.querySelector('.self-notice__message')?.textContent?.trim()).toBe(message);
      expect(encadre!.querySelector('.self-notice__hint')?.textContent?.trim()).toBe('Contactez la RH.');
      expect(encadre!.getAttribute('data-kind')).toBe('account');
      // Les cartes sont remplacées par l'encadré : aucun « Aucune … sur cette période » trompeur, aucun titre de carte.
      expect(el.querySelectorAll('h2')).toHaveLength(0);
      expect(el.textContent).not.toContain('sur cette période');
      expect(el.textContent).not.toContain('Ces données n’ont pas pu être chargées.');
    });
  }

  it('403 matricule changé : consigne « Reconnectez-vous. »', () => {
    const message = 'Le matricule de ce compte a changé depuis la connexion : reconnectez-vous.';
    const { encadre } = monter(refus(message));

    expect(encadre!.querySelector('.self-notice__message')?.textContent?.trim()).toBe(message);
    expect(encadre!.querySelector('.self-notice__hint')?.textContent?.trim()).toBe('Reconnectez-vous.');
    expect(encadre!.getAttribute('data-kind')).toBe('reconnect');
  });

  it('403 compte supprimé : consigne « Contactez la RH. » (le message ne demande pas de se reconnecter)', () => {
    const { encadre } = monter(refus('Ce compte n\'existe plus : contactez la RH.'));

    expect(encadre!.querySelector('.self-notice__hint')?.textContent?.trim()).toBe('Contactez la RH.');
  });

  it('le message est un texte seul : jamais de JSON brut, de balise ni de détail technique', () => {
    for (const erreur of [
      { status: 403, error: '{"error":"Ce matricule est inconnu dans la base"}' },
      { status: 403, error: '<html><body>Forbidden</body></html>' },
      { status: 403, error: { detail: 'trace interne' } },
    ]) {
      const { el, encadre } = monter(erreur);
      const texte = encadre!.textContent ?? '';

      expect(texte).not.toContain('{');
      expect(texte).not.toContain('<');
      expect(texte).not.toContain('trace interne');
      expect(el.textContent).toContain('Contactez la RH.');
    }
  });

  it('l\'encadré reste affiché (persistant) et le sélecteur de mois reste disponible', () => {
    const { fixture, el } = monter(refus('Ce matricule est inconnu dans la base : contactez la RH.'));
    fixture.detectChanges();
    fixture.detectChanges();

    expect(el.querySelector('.self-notice')).not.toBeNull();
    expect(el.querySelectorAll('select')).toHaveLength(1);
    expect(el.querySelector('button')).toBeNull();
    expect(el.querySelector('a')).toBeNull();
  });
});

describe('Mon espace : encadré durable des autres erreurs', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('400 période : message du serveur et consigne « Choisissez un autre mois. », cartes masquées', () => {
    const { el, encadre } = monter({ status: 400, error: { error: 'Seuls les 4 derniers mois sont consultables.' } });

    expect(encadre!.querySelector('.self-notice__message')?.textContent?.trim()).toBe('Seuls les 4 derniers mois sont consultables.');
    expect(encadre!.querySelector('.self-notice__hint')?.textContent?.trim()).toBe('Choisissez un autre mois.');
    expect(el.querySelectorAll('h2')).toHaveLength(0);
  });

  it('400 sans message exploitable (erreur de modèle) : message neutre, jamais le détail brut', () => {
    const { encadre } = monter({ status: 400, error: { errors: { From: ['The value is not valid.'] } } });

    expect(encadre!.querySelector('.self-notice__message')?.textContent?.trim()).toBe('La période demandée n’est pas valide.');
    expect(encadre!.textContent).not.toContain('The value');
  });

  it('500 : consigne « Réessayez dans un instant. » ; les cartes restent, chacune avec son message neutre', () => {
    const { el, encadre } = monter({ status: 500, error: { error: 'Erreur interne du serveur.' } });

    expect(encadre!.querySelector('.self-notice__message')?.textContent?.trim()).toBe('Erreur interne du serveur.');
    expect(encadre!.querySelector('.self-notice__hint')?.textContent?.trim()).toBe('Réessayez dans un instant.');
    expect(el.querySelectorAll('h2')).toHaveLength(4);
    expect(el.textContent).toContain('Ces données n’ont pas pu être chargées.');
  });

  it('500 sans corps : message neutre « Mon espace est momentanément indisponible. »', () => {
    const { encadre } = monter({ status: 500, error: null });

    expect(encadre!.querySelector('.self-notice__message')?.textContent?.trim()).toBe('Mon espace est momentanément indisponible.');
    expect(encadre!.querySelector('.self-notice__hint')?.textContent?.trim()).toBe('Réessayez dans un instant.');
  });

  it('réseau (statut 0) : « Le serveur est injoignable. » et consigne « Réessayez dans un instant. »', () => {
    const { encadre } = monter({ status: 0, error: new ProgressEvent('error') });

    expect(encadre!.querySelector('.self-notice__message')?.textContent?.trim()).toBe('Le serveur est injoignable.');
    expect(encadre!.querySelector('.self-notice__hint')?.textContent?.trim()).toBe('Réessayez dans un instant.');
    expect(encadre!.textContent).not.toContain('ProgressEvent');
  });

  it('une seule carte en erreur serveur : l\'encadré s\'ajoute, les trois autres cartes restent affichées', () => {
    const { el, encadre } = monter({ status: 500, error: null }, 'absences');

    expect(encadre).not.toBeNull();
    expect(el.textContent).toContain('Aucune anomalie sur cette période.');
    expect(el.querySelectorAll('h2')).toHaveLength(4);
  });

  it('quatre erreurs différentes en même temps : l\'erreur de compte l\'emporte sur le réseau', () => {
    TestBed.resetTestingModule();
    const erreurs: Record<Methode, unknown> = {
      weeks: { status: 0, error: null },
      anomalies: { status: 500, error: null },
      absences: { status: 403, error: { error: 'Ce compte n\'a pas accès à Mon espace.' } },
      delays: { status: 0, error: null },
    };
    const lecture = (m: Methode) => () => throwError(() => erreurs[m]);
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: SelfSpaceService, useValue: { weeks: lecture('weeks'), anomalies: lecture('anomalies'), absences: lecture('absences'), delays: lecture('delays') } },
      ],
    });
    const fixture = TestBed.createComponent(SelfSpacePage);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;

    expect(el.querySelectorAll('.self-notice')).toHaveLength(1);
    expect(el.querySelector('.self-notice__hint')?.textContent?.trim()).toBe('Contactez la RH.');
    expect(el.querySelectorAll('h2')).toHaveLength(0);
  });

  it('aucune requête hors api/me/space/* : seules les quatre lectures de Mon espace sont émises', () => {
    const { appels } = monter({ status: 403, error: { error: 'Ce compte n\'a pas accès à Mon espace.' } });

    expect(appels.sort()).toEqual(['absences', 'anomalies', 'delays', 'weeks']);
  });
});
