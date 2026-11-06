import { TestBed } from '@angular/core/testing';
import { Router, UrlTree, provideRouter } from '@angular/router';
import { Observable, of, Subject, throwError } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { SessionStore } from '../../core/session.store';
import { selfSpaceGuard } from './self-space.guard';
import { SelfSpacePage } from './self-space';
import { SelfAbsences, SelfAnomalies, SelfDelays, SelfSpaceService, SelfWeeks } from './self-space.service';

const periode = { from: '2026-11-01', to: '2026-11-06' };
const semaines: SelfWeeks = {
  periode,
  semaines: [
    { lundi: '2026-10-26T00:00:00', dimanche: '2026-11-01T00:00:00', heuresTravaillees: 45, heuresSupplementaires: 10, hs130: 8, hs150: 2, nuit: 0, dimanches: 0, feries: 0, retard: 0, absence: 0 },
  ],
  exoImpo: [{ periodeDebut: '2026-10-01T00:00:00', periodeFin: '2026-10-31T00:00:00', exo130: 8, exo150: 2, imp130: 0, imp150: 0 }],
};
const anomalies: SelfAnomalies = { periode, anomalies: [{ date: '2026-11-03T00:00:00', nature: 'Pointage à vérifier', heure1: '08:00:00', heure2: null, ferie: false }] };
const absences: SelfAbsences = { periode, absences: [{ date: '2026-11-02T00:00:00', periode: 'Journée', type: 'Congé SAGE', dureeHeures: 8, motif: null }] };
const retards: SelfDelays = { periode, retards: [{ date: '2026-11-04T00:00:00', heureEntree: '08:20:00', heurePrevue: '08:00:00', toleranceMinutes: 0, minutesRetard: 20, retardSaisi: false }] };

function monter(over: Partial<Record<'weeks' | 'anomalies' | 'absences' | 'delays', () => Observable<unknown>>> = {}) {
  TestBed.resetTestingModule();
  const appels: { methode: string; from: string; to: string }[] = [];
  const faux = (methode: string, donnees: unknown) => (r: { from: string; to: string }) => {
    appels.push({ methode, from: r.from, to: r.to });
    return (over as Record<string, (() => Observable<unknown>) | undefined>)[methode]?.() ?? of(donnees);
  };
  // Seul SelfSpaceService est fourni : sans HttpClient, tout appel vers une autre API ferait échouer le test.
  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      {
        provide: SelfSpaceService,
        useValue: { weeks: faux('weeks', semaines), anomalies: faux('anomalies', anomalies), absences: faux('absences', absences), delays: faux('delays', retards) },
      },
    ],
  });
  const fixture = TestBed.createComponent(SelfSpacePage);
  fixture.detectChanges();
  return { fixture, el: fixture.nativeElement as HTMLElement, appels };
}

describe('Mon espace : page', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('quatre cartes en texte : heures et EXO/IMPO, anomalies, absences et congés, retards', () => {
    const { el } = monter();
    const titres = Array.from(el.querySelectorAll('h2')).map((h) => h.textContent?.trim());

    expect(titres).toEqual(['Mes heures par semaine', 'Mes anomalies', 'Mes absences et congés', 'Mes retards']);
    expect(el.textContent).toContain('26/10/2026 au 01/11/2026');
    expect(el.textContent).toContain('10 h');
    expect(el.textContent).toContain('Exonérées 130 %');
    expect(el.textContent).toContain('Pointage à vérifier');
    expect(el.textContent).toContain('Congé SAGE');
    expect(el.textContent).toContain('20 min de retard');
  });

  it('aucun lien, aucun bouton d’export, aucun contrôle autre que le sélecteur de mois', () => {
    const { el } = monter();

    expect(el.querySelector('a')).toBeNull();
    expect(el.querySelector('button')).toBeNull();
    expect(el.querySelectorAll('select')).toHaveLength(1);
    expect(el.querySelector('input')).toBeNull();
  });

  it('quatre lectures au chargement, mois en cours, uniquement la période (jamais de matricule)', () => {
    const { appels } = monter();

    expect(appels.map((a) => a.methode).sort()).toEqual(['absences', 'anomalies', 'delays', 'weeks']);
    expect(new Set(appels.map((a) => `${a.from}|${a.to}`)).size).toBe(1);
    expect(appels[0].from).toMatch(/^\d{4}-\d{2}-01$/);
  });

  it('changer de mois relance les quatre lectures avec la nouvelle période et annule les précédentes', () => {
    const attentes: Subject<SelfWeeks>[] = [];
    const { fixture, el, appels } = monter({ weeks: () => { const f = new Subject<SelfWeeks>(); attentes.push(f); return f; } });
    const avant = appels.length;
    const select = el.querySelector<HTMLSelectElement>('select')!;

    select.value = select.options[1].value;
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    expect(appels.length).toBe(avant + 4);
    expect(attentes).toHaveLength(2);
    expect(attentes[0].observed).toBe(false);                               // la lecture en attente du mois précédent a été annulée
    expect(attentes[1].observed).toBe(true);
    const derniere = appels.slice(avant);
    expect(new Set(derniere.map((a) => a.from)).size).toBe(1);
    expect(derniere[0].from).not.toBe(appels[0].from);
  });

  it('une carte en échec n’empêche pas les autres ; message neutre, sans détail technique', () => {
    const { el } = monter({ absences: () => throwError(() => ({ status: 500 })) });

    expect(el.textContent).toContain('Ces données n’ont pas pu être chargées.');
    expect(el.textContent).toContain('Pointage à vérifier');                // les autres cartes sont affichées
    expect(el.querySelectorAll('[role="alert"]')).toHaveLength(1);
  });

  it('états vides lisibles', () => {
    const { el } = monter({
      weeks: () => of({ periode, semaines: [], exoImpo: [] }),
      anomalies: () => of({ periode, anomalies: [] }),
      absences: () => of({ periode, absences: [] }),
      delays: () => of({ periode, retards: [] }),
    });

    expect(el.textContent).toContain('Aucune semaine validée sur cette période.');
    expect(el.textContent).toContain('Aucun calcul EXO / IMPO sur cette période.');
    expect(el.textContent).toContain('Aucune anomalie sur cette période.');
    expect(el.textContent).toContain('Aucune absence ni aucun congé sur cette période.');
    expect(el.textContent).toContain('Aucun retard sur cette période.');
  });

  it('chargement annoncé (aria-busy) tant que les données n’arrivent pas', () => {
    const { el } = monter({ weeks: () => new Subject<SelfWeeks>() });

    expect(el.querySelector('[aria-labelledby="self-hours"]')?.getAttribute('aria-busy')).toBe('true');
    expect(el.textContent).toContain('Chargement…');
  });
});

describe('Mon espace : garde de route', () => {
  beforeEach(() => {
    localStorage.clear();
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [provideRouter([])] });
  });
  afterEach(() => {
    localStorage.clear();
    TestBed.resetTestingModule();
  });

  const connecter = (permissions: string[]) =>
    TestBed.inject(SessionStore).setSession({
      token: 'jeton', userId: 1, login: 'x', nom: 'X', roleId: 2, matricule: null, rights: [], authorizedDatabases: [], permissions, permissionsCustomized: true,
    });
  const garde = () => TestBed.runInInjectionContext(() => selfSpaceGuard({} as never, {} as never));

  it('laisse passer SELF:READ', () => {
    connecter(['SELF:READ']);
    expect(garde()).toBe(true);
  });

  it('renvoie vers l’accueil sans SELF:READ (compte de gestion ou administrateur)', () => {
    connecter(['PROC:SHIFTS']);
    const resultat = garde();
    expect(resultat).toBeInstanceOf(UrlTree);
    expect(TestBed.inject(Router).serializeUrl(resultat as UrlTree)).toBe('/');
  });

  it('refuse après la déconnexion', () => {
    connecter(['SELF:READ']);
    TestBed.inject(SessionStore).clear();
    expect(garde()).toBeInstanceOf(UrlTree);
  });
});
