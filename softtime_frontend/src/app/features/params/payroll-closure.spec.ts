import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ConfirmService } from '../../core/confirm.service';
import { Droit } from '../../core/rights';
import { SessionStore } from '../../core/session.store';
import { ToastService } from '../../core/toast.service';
import { PayrollPeriods, PayrollPeriodState, PayrollSendSummary } from '../../shared/models';
import { PayrollClosureService } from '../../shared/services/payroll-closure.service';
import { ClosedPeriodBanner, texteBandeau, VERROU_CLOTURE } from '../../shared/components/closed-period-banner';
import { PayrollClosureCard } from './payroll-closure';
import {
  aujourdhuiIso,
  avertissement,
  dateFr,
  empreinteCourte,
  libelleEtat,
  libelleFiltre,
  questionCloture,
} from './payroll-closure.logic';

const EMPREINTE = 'a'.repeat(20) + 'b'.repeat(44);

function envoi(champs: Partial<PayrollSendSummary> = {}): PayrollSendSummary {
  return {
    envoiId: 7, bddSage: 'SAGE_DEMO', periodeDebut: '2026-08-17', periodeFin: '2026-08-23', typeBase: 'STANDARD',
    envoyeLe: '2026-08-25T08:00:00', envoyePar: 'rh', statut: 'SUCCES', lignesEnvoi: 3, instantaneId: 4, avecFiltre: false,
    empreinte: EMPREINTE, nbLignes: 3, ...champs,
  };
}

function periode(champs: Partial<PayrollPeriodState> = {}, e: Partial<PayrollSendSummary> = {}): PayrollPeriodState {
  return { periodeDebut: '2026-08-17', periodeFin: '2026-08-23', etat: 'ENVOYEE', dernierEnvoi: envoi(e), cloture: null, ...champs };
}

describe('Clôture de paie : règles de l\'écran', () => {
  it('formate dates, états, filtre et empreinte raccourcie', () => {
    expect(dateFr('2026-08-17T00:00:00')).toBe('17/08/2026');
    expect(libelleEtat('ENVOYEE')).toBe('Envoyée');
    expect(libelleEtat('CLOTUREE')).toBe('Clôturée');
    expect(empreinteCourte(EMPREINTE)).toBe('aaaaaaaaaaaa…');
    expect(libelleFiltre(envoi({ departement: 'BR704' }))).toBe('département BR704');
    expect(libelleFiltre(envoi())).toBe('');
    expect(aujourdhuiIso(new Date(2026, 10, 18))).toBe('2026-11-18');
  });

  it('la confirmation rappelle la période, l\'absence de retour en arrière et la correction sur la période suivante', () => {
    const q = questionCloture('SAGE_DEMO', periode());
    expect(q).toContain('Clôturer la période du 17/08/2026 au 23/08/2026 de la base SAGE_DEMO ?');
    expect(q).toContain("il n'y a pas de retour en arrière");
    expect(q).toContain('Les corrections se feront sur la période suivante.');
    expect(q).toContain('empreinte aaaaaaaaaaaa…');
  });

  it('avertit des conditions connues qui manquent (le serveur reste seul juge)', () => {
    const jour = '2026-11-18';
    expect(avertissement(periode(), jour)).toBeNull();
    expect(avertissement(periode({}, { avecFiltre: true, departement: 'BR704' }), jour)).toBe('Dernier envoi filtré : lancez un envoi sans filtre avant de clôturer.');
    expect(avertissement(periode({}, { statut: 'INCERTAIN' }), jour)).toContain('incertain');
    expect(avertissement(periode({}, { statut: 'ECHEC' }), jour)).toContain('en échec');
    expect(avertissement(periode({}, { instantaneId: null }), jour)).toContain('sans instantané');
    expect(avertissement(periode({ periodeFin: '2026-11-18' }), jour)).toContain('Période non terminée');
    expect(avertissement(periode({ etat: 'CLOTUREE' }), jour)).toBeNull();
  });

  it('le texte du bandeau ne répète pas le titre et tient en une phrase', () => {
    const t = texteBandeau('2026-08-17', '2026-08-23');
    expect(t).toBe("Du 17/08/2026 au 23/08/2026, aucune modification n'est possible : les corrections se font sur la période suivante.");
    expect(t).not.toContain('Période clôturée');
    expect(t.match(/\./g)?.length).toBe(1);
    expect(VERROU_CLOTURE).toContain('définitive');
  });
});

function monter(options: { donnees?: PayrollPeriods; permissions?: string[]; confirme?: boolean; refus?: string } = {}) {
  const donnees = options.donnees ?? { bddSage: 'SAGE_DEMO', peutCloturer: true, periodes: [periode()] };
  const svc = {
    periods: vi.fn(() => of(donnees)),
    close: vi.fn(() => (options.refus
      ? throwError(() => ({ status: 409, error: { message: options.refus } }))
      : of({ id: 1, empreinte: EMPREINTE }))),
    state: vi.fn(),
  };
  const toast = { success: vi.fn(), error: vi.fn(), info: vi.fn(), warning: vi.fn() };
  const confirm = { ask: vi.fn(async () => options.confirme ?? true) };
  TestBed.configureTestingModule({
    imports: [PayrollClosureCard],
    providers: [
      { provide: PayrollClosureService, useValue: svc },
      {
        provide: SessionStore,
        useValue: {
          activeSageDb: () => 'SAGE_DEMO',
          permissions: () => options.permissions ?? ['SAGE:SAGE_DEMO:CLOTURE'],
          hasRight: (d: number) => d === Droit.Databases,
        },
      },
      { provide: ToastService, useValue: toast },
      { provide: ConfirmService, useValue: confirm },
    ],
  });
  const fixture = TestBed.createComponent(PayrollClosureCard);
  fixture.detectChanges();
  return { fixture, carte: fixture.componentInstance, svc, toast, confirm, texte: () => (fixture.nativeElement as HTMLElement).textContent ?? '' };
}

describe('Carte « Clôture de paie »', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('liste les périodes envoyées avec leur état, leur empreinte raccourcie et le bouton Clôturer', () => {
    const { texte, fixture } = monter();
    expect(texte()).toContain('du 17/08/2026 au 23/08/2026');
    expect(texte()).toContain('Envoyée');
    expect(texte()).toContain('aaaaaaaaaaaa…');
    const boutons = [...(fixture.nativeElement as HTMLElement).querySelectorAll('button')].map((b) => b.textContent?.trim());
    expect(boutons).toContain('Clôturer');
  });

  it('une période clôturée affiche son état, son auteur, et aucun bouton de déblocage', () => {
    const cloturee = periode({
      etat: 'CLOTUREE',
      cloture: { id: 1, bddSage: 'SAGE_DEMO', periodeDebut: '2026-08-17', periodeFin: '2026-08-23', clotureLe: '2026-11-18T09:00:00',
        cloturePar: 'paie.rh', envoiId: 7, instantaneId: 4, empreinte: EMPREINTE, statutControle: 'Pret', resumeControle: 'Statut=Prêt' },
    });
    const { texte, fixture } = monter({ donnees: { bddSage: 'SAGE_DEMO', peutCloturer: true, periodes: [cloturee] } });
    expect(texte()).toContain('Clôturée');
    expect(texte()).toContain('par paie.rh');
    expect(texte()).toContain('Définitive');
    const boutons = [...(fixture.nativeElement as HTMLElement).querySelectorAll('button')].map((b) => (b.textContent ?? '').trim().toLowerCase());
    expect(boutons).not.toContain('clôturer');
    expect(boutons.some((b) => /débloq|rouvrir|annuler la clôture|déclôtur/.test(b))).toBe(false);
  });

  it('sans le droit de clôture, le bouton n\'apparaît pas', () => {
    const { fixture, texte } = monter({
      permissions: ['SAGE:SAGE_DEMO:UPDATE'],
      donnees: { bddSage: 'SAGE_DEMO', peutCloturer: false, periodes: [periode()] },
    });
    const boutons = [...(fixture.nativeElement as HTMLElement).querySelectorAll('button')].map((b) => b.textContent?.trim());
    expect(boutons).not.toContain('Clôturer');
    expect(texte()).toContain('Clôture réservée');
  });

  it('rien n\'est envoyé sans confirmation', async () => {
    const { carte, svc, confirm } = monter({ confirme: false });
    await carte.cloturer(periode());
    expect(confirm.ask).toHaveBeenCalledWith(expect.stringContaining("pas de retour en arrière"), 'Clôturer la période de paie', 'Clôturer définitivement');
    expect(svc.close).not.toHaveBeenCalled();
  });

  it('clôture confirmée : appel avec la période exacte puis relecture', async () => {
    const { carte, svc, toast } = monter();
    await carte.cloturer(periode());
    expect(svc.close).toHaveBeenCalledWith('2026-08-17', '2026-08-23');
    expect(toast.success).toHaveBeenCalledWith(expect.stringContaining('clôturée'));
    expect(svc.periods).toHaveBeenCalledTimes(2);
  });

  it('le refus du serveur est affiché tel quel', async () => {
    const refus = 'Contrôle avant paie « À corriger » sur la base entière : clôture refusée, le statut « Prêt » est exigé. Points à corriger : 2 anomalie(s) critique(s).';
    const { carte, fixture, texte } = monter({ refus });
    await carte.cloturer(periode());
    fixture.detectChanges();
    expect(carte.erreur()).toBe(refus);
    expect(texte()).toContain(refus);
  });

  it('vue étroite : la carte garde un tableau présenté en cartes sous 768 px', () => {
    const styles = (PayrollClosureCard as unknown as { ɵcmp: { styles: string[] } }).ɵcmp.styles.join('');
    expect(styles).toContain('max-width: 767px');
    expect(styles).toContain('attr(data-label)');
  });
});

describe('Bandeau « Période clôturée »', () => {
  afterEach(() => TestBed.resetTestingModule());

  function bandeau(cloturee: boolean) {
    const svc = {
      state: vi.fn(() => of(cloturee
        ? { cloturee: true, periodes: [{ debut: '2026-08-17', fin: '2026-08-23' }], message: 'Période clôturée (du 17/08/2026 au 23/08/2026) : modification impossible. Les corrections se font sur la période suivante.' }
        : { cloturee: false, periodes: [], message: null })),
    };
    TestBed.configureTestingModule({ imports: [ClosedPeriodBanner], providers: [{ provide: PayrollClosureService, useValue: svc }] });
    const fixture = TestBed.createComponent(ClosedPeriodBanner);
    const emis: boolean[] = [];
    fixture.componentInstance.closedChange.subscribe((v) => emis.push(v));
    fixture.componentRef.setInput('period', { from: '2026-08-01', to: '2026-08-31' });
    fixture.detectChanges();
    fixture.detectChanges();
    return { fixture, svc, emis, texte: () => (fixture.nativeElement as HTMLElement).textContent ?? '' };
  }

  it('sélection qui recoupe une période clôturée : bandeau affiché et pages averties', () => {
    const { fixture, texte, emis, svc } = bandeau(true);
    expect(svc.state).toHaveBeenCalledWith('2026-08-01', '2026-08-31');
    expect((fixture.nativeElement as HTMLElement).querySelector('.cpb__title')?.textContent?.trim()).toBe('Période clôturée');
    expect((fixture.nativeElement as HTMLElement).querySelector('.cpb__text')?.textContent?.trim())
      .toBe("Du 17/08/2026 au 23/08/2026, aucune modification n'est possible : les corrections se font sur la période suivante.");
    expect(texte().match(/Période clôturée/g)?.length).toBe(1);
    expect(emis.at(-1)).toBe(true);
  });

  it('période ouverte : aucun bandeau, boutons actifs', () => {
    const { texte, emis } = bandeau(false);
    expect(texte().trim()).toBe('');
    expect(emis.at(-1)).toBe(false);
  });
});

describe('Carte « Clôture de paie » : noms accessibles', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('le bouton Clôturer porte le nom de sa période sur le <button> lui-même', () => {
    const { fixture } = monter();
    const bouton = [...(fixture.nativeElement as HTMLElement).querySelectorAll('button')].find((b) => b.textContent?.trim() === 'Clôturer')!;
    expect(bouton.getAttribute('aria-label')).toBe('Clôturer la période du 17/08/2026 au 23/08/2026');
  });
});

describe('Carte « Clôture de paie » : visibilité', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('sans droit de modifier ni de clôturer la base, la carte n\'apparaît pas et rien n\'est lu', () => {
    const { texte, svc } = monter({ permissions: ['SAGE:SAGE_DEMO:READ'] });
    expect(texte().trim()).toBe('');
    expect(svc.periods).not.toHaveBeenCalled();
  });
});
