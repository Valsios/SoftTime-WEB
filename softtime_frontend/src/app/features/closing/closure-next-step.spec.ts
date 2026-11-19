import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { Droit } from '../../core/rights';
import { SessionStore } from '../../core/session.store';
import { PayrollDomainState, PayrollPeriods, PayrollPeriodState, PayrollSendSummary } from '../../shared/models';
import { PayrollClosureService } from '../../shared/services/payroll-closure.service';
import {
  AUCUN_ENVOI_BORNES,
  etapeSuivante,
  etatDomaineCourt,
  peutVoirCloture,
  RAPPEL_CLOTURE_BASE_ENTIERE,
  TITRE_CARTE_CLOTURE,
} from '../params/payroll-closure.logic';
import { ClosureNextStep } from './closure-next-step';

const EMPREINTE = 'a'.repeat(64);

function envoi(champs: Partial<PayrollSendSummary> = {}): PayrollSendSummary {
  return {
    envoiId: 7, bddSage: 'SAGE_DEMO', periodeDebut: '2026-08-17', periodeFin: '2026-08-23', typeBase: 'STANDARD',
    envoyeLe: '2026-08-25T08:00:00', envoyePar: 'rh', statut: 'SUCCES', lignesEnvoi: 3, instantaneId: 4, avecFiltre: false,
    empreinte: EMPREINTE, nbLignes: 3, ...champs,
  };
}

function periode(champs: Partial<PayrollPeriodState> = {}): PayrollPeriodState {
  return { periodeDebut: '2026-08-17', periodeFin: '2026-08-23', etat: 'ENVOYEE', dernierEnvoi: envoi(), cloture: null, ...champs };
}

function dom(domaine: PayrollDomainState['domaine'], libelle: string, etat: PayrollDomainState['etat']): PayrollDomainState {
  return { domaine, libelle, actif: true, exige: etat !== 'NON_EXIGE', etat };
}

const JOUR = '2026-11-18';

const CLOTURE = {
  id: 1, bddSage: 'SAGE_DEMO', periodeDebut: '2026-08-17', periodeFin: '2026-08-23', clotureLe: '2026-08-26T09:30:00Z',
  cloturePar: 'rh', envoiId: 7, instantaneId: 4, empreinte: EMPREINTE, statutControle: 'Pret', resumeControle: '',
};

describe('Étape suivante : logique (réutilise la logique de la carte)', () => {
  it('phrase « Heures : envoyées, Cantine : manquante »', () => {
    const p = periode({ domaines: [dom('HEURES', 'Heures', 'ENVOYE'), dom('CANTINE', 'Cantine', 'MANQUANT'), dom('FRAIS', 'Frais de déplacement', 'NON_EXIGE')] });
    const e = etapeSuivante([p], '2026-08-17', '2026-08-23', JOUR);
    expect(e.domaines).toEqual(['Heures : envoyées', 'Cantine : manquante']);
    expect(e.phrase).toBe('Période envoyée, pas encore clôturée. Heures : envoyées, Cantine : manquante.');
    expect(e.indication).toContain('Cantine');
  });

  it('période non envoyée sur ces bornes exactes', () => {
    expect(etapeSuivante([periode()], '2026-08-17', '2026-08-30', JOUR)).toMatchObject({ phrase: AUCUN_ENVOI_BORNES, aucunEnvoi: true, etat: 'AUCUN' });
    expect(etapeSuivante([], '2026-08-17', '2026-08-23', JOUR).aucunEnvoi).toBe(true);
  });

  it('période clôturée sans domaines (serveur ancien) : heures seulement', () => {
    const e = etapeSuivante([periode({ etat: 'CLOTUREE', cloture: CLOTURE })], '2026-08-17', '2026-08-23', JOUR);
    expect(e.etat).toBe('CLOTUREE');
    expect(e.phrase).toContain('Période clôturée le ');
    expect(e.phrase).toContain('Heures : envoyées');
    expect(e.indication).toBeNull();
  });

  it('accords des états de domaine', () => {
    expect(etatDomaineCourt(dom('FRAIS', 'Frais', 'FIGE'))).toBe('Frais : figées');
    expect(etatDomaineCourt(dom('CANTINE', 'Cantine', 'ENVOYE'))).toBe('Cantine : envoyée');
  });

  it('mêmes conditions de visibilité que la carte', () => {
    expect(peutVoirCloture(null, ['SAGE:X:CLOTURE'], true)).toBe(false);
    expect(peutVoirCloture('X', [], true)).toBe(true);
    expect(peutVoirCloture('X', [], false)).toBe(false);
    expect(peutVoirCloture('X', ['SAGE:X:CLOTURE'], false)).toBe(true);
    expect(peutVoirCloture('X', ['SAGE:X:UPDATE'], false)).toBe(true);
    expect(peutVoirCloture('X', ['SAGE:Y:CLOTURE'], false)).toBe(false);
  });
});

function monter(options: { base?: string | null; permissions?: string[]; donnees?: PayrollPeriods; du?: string; au?: string } = {}) {
  const donnees = options.donnees ?? { bddSage: 'SAGE_DEMO', peutCloturer: true, periodes: [periode()] };
  const svc = { periods: vi.fn(() => of(donnees)), close: vi.fn(), state: vi.fn() };
  const base = signal<string | null>(options.base === undefined ? 'SAGE_DEMO' : options.base);
  TestBed.configureTestingModule({
    imports: [ClosureNextStep],
    providers: [
      provideRouter([]),
      { provide: PayrollClosureService, useValue: svc },
      {
        provide: SessionStore,
        useValue: {
          activeSageDb: () => base(),
          permissions: () => options.permissions ?? ['SAGE:SAGE_DEMO:CLOTURE'],
          hasRight: (d: number) => d === Droit.Databases,
        },
      },
    ],
  });
  const fixture = TestBed.createComponent(ClosureNextStep);
  fixture.componentRef.setInput('from', options.du ?? '2026-08-17');
  fixture.componentRef.setInput('to', options.au ?? '2026-08-23');
  fixture.detectChanges();
  const el = fixture.nativeElement as HTMLElement;
  return { fixture, svc, el, texte: () => el.textContent ?? '' };
}

describe('Bloc « Étape suivante : clôture »', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('visible avec le droit : titre, état, rappel, lien vers la carte', () => {
    const { el, texte, svc } = monter();
    expect(svc.periods).toHaveBeenCalledTimes(1);
    expect(texte()).toContain('Étape suivante : clôture');
    expect(texte()).toContain('Période envoyée, pas encore clôturée. Heures : envoyées.');
    expect(texte()).toContain(RAPPEL_CLOTURE_BASE_ENTIERE);
    const lien = el.querySelector('a') as HTMLAnchorElement;
    expect(lien.textContent?.trim()).toBe('Aller à la clôture');
    expect(lien.getAttribute('href')).toBe('/sage-databases#cloture-paie');
    expect(lien.getAttribute('aria-label')).toContain(TITRE_CARTE_CLOTURE);
  });

  it('masqué sans base active, sans appel serveur', () => {
    const { el, svc } = monter({ base: null });
    expect(el.querySelector('soft-card')).toBeNull();
    expect(svc.periods).not.toHaveBeenCalled();
  });

  it('masqué sans droit sur la base', () => {
    const { el, svc } = monter({ permissions: ['SAGE:AUTRE:CLOTURE'] });
    expect(el.querySelector('soft-card')).toBeNull();
    expect(svc.periods).not.toHaveBeenCalled();
  });

  it('période non envoyée : « Aucun envoi sur ces bornes exactes »', () => {
    const { texte } = monter({ au: '2026-08-30' });
    expect(texte()).toContain('Aucun envoi sur ces bornes exactes.');
    expect(texte()).toContain('Non envoyée');
  });

  it('période clôturée', () => {
    const { texte } = monter({ donnees: { bddSage: 'SAGE_DEMO', peutCloturer: true, periodes: [periode({ etat: 'CLOTUREE', cloture: CLOTURE })] } });
    expect(texte()).toContain('Période clôturée le ');
  });

  it('domaine manquant', () => {
    const p = periode({ domaines: [dom('HEURES', 'Heures', 'ENVOYE'), dom('CANTINE', 'Cantine', 'MANQUANT')] });
    const { texte } = monter({ donnees: { bddSage: 'SAGE_DEMO', peutCloturer: true, periodes: [p] } });
    expect(texte()).toContain('Heures : envoyées, Cantine : manquante');
  });

  it('lecture seule : aucun bouton, aucun appel d\'écriture', () => {
    const { el, svc } = monter();
    expect(el.querySelectorAll('button').length).toBe(0);
    expect(Array.from(el.querySelectorAll('a')).map((a) => a.textContent?.trim())).toEqual(['Aller à la clôture']);
    expect(svc.close).not.toHaveBeenCalled();
  });

  it('vue étroite : lien pleine largeur sous 768 px et texte enveloppé', () => {
    const styles = (ClosureNextStep as unknown as { ɵcmp: { styles: string[] } }).ɵcmp.styles.join('');
    expect(styles).toContain('max-width: 767px');
    expect(styles).toContain('justify-self: stretch');
    expect(styles).toContain('overflow-wrap: anywhere');
  });
});
