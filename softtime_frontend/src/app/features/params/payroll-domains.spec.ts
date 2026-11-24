import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ConfirmService } from '../../core/confirm.service';
import { SessionStore } from '../../core/session.store';
import { ToastService } from '../../core/toast.service';
import { PayrollVariablesConfig } from '../../shared/models';
import { PayrollVariablesService } from '../../shared/services/payroll-variables.service';
import { MESSAGE_AUCUNE_CONSTANTE, questionDomaines } from './payroll-domains';
import { PayrollVariablesCard } from './payroll-variables';

const CONSTANTES = [{ code: 'CANT01', intitule: 'Cantine salariés' }, { code: 'HA06', intitule: 'Frais' }];

function config(champs: Partial<PayrollVariablesConfig> = {}): PayrollVariablesConfig {
  return {
    base: 'SAGE_DEMO', typeBase: 'STANDARD', parDefaut: false, moisEnvoye: true, dernierEnvoi: '2026-11-01 → 2026-11-30',
    variables: [], colonnesCibles: [], codesPaie: CONSTANTES,
    domaines: { cantine: false, frais: false, codeCantine: 'CANTINE_CODE', codeCantineParDefaut: true }, ...champs,
  };
}

/** La carte rendue : les lignes Cantine et Frais font partie de son tableau. */
function monter(options: { config?: PayrollVariablesConfig; confirme?: boolean; refus?: string } = {}) {
  const svc = {
    get: vi.fn(() => of(options.config ?? config())),
    destinations: vi.fn(() => of({ base: 'SAGE_DEMO', table: null, colonneMatricule: null, variables: [], tables: [], colonnes: [], moisEnvoye: false, dernierEnvoi: null })),
    saveDomains: vi.fn(() => (options.refus
      ? throwError(() => ({ status: 400, error: { message: options.refus } }))
      : of(config({ domaines: { cantine: true, frais: false } })))),
  };
  const toast = { success: vi.fn(), error: vi.fn(), info: vi.fn(), warning: vi.fn() };
  const confirm = { ask: vi.fn(async () => options.confirme ?? true) };
  TestBed.configureTestingModule({
    imports: [PayrollVariablesCard],
    providers: [
      { provide: PayrollVariablesService, useValue: svc },
      { provide: SessionStore, useValue: { activeSageDb: () => 'SAGE_DEMO', permissions: () => ['SAGE:SAGE_DEMO:UPDATE'], hasRight: () => true } },
      { provide: ToastService, useValue: toast },
      { provide: ConfirmService, useValue: confirm },
    ],
  });
  const fixture = TestBed.createComponent(PayrollVariablesCard);
  fixture.detectChanges();
  const el = fixture.nativeElement as HTMLElement;
  const d = fixture.componentInstance.domaines;
  const interrupteur = (nom: string) => el.querySelector<HTMLInputElement>(`input[aria-label="Envoyer ${nom} à la paie"]`)!;
  const ligne = (libelle: string) => [...el.querySelectorAll('tbody tr')].find((tr) => tr.querySelector('.pv-name__text')?.textContent?.trim().replace(/ /g, ' ') === libelle)!;
  const bouton = () => [...el.querySelectorAll('.pv-domains-save button')][0] as HTMLButtonElement;
  return { fixture, d, svc, toast, confirm, el, interrupteur, ligne, bouton };
}

describe('Domaines envoyés à la paie (cantine, frais) : lignes du tableau des variables', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('désactivés par défaut : deux lignes, badge « non envoyé à la paie », interrupteurs éteints, enregistrement inactif', () => {
    const { interrupteur, ligne, bouton } = monter({ config: config({ domaines: null }) });
    expect(interrupteur('la cantine').checked).toBe(false);
    expect(interrupteur('les frais de déplacement').checked).toBe(false);
    expect(ligne('Cantine').textContent).toContain('non envoyé à la paie');
    expect(ligne('Frais de déplacement').textContent).toContain('non envoyé à la paie');
    expect(bouton().disabled).toBe(true);
  });

  it('reprend le réglage enregistré, badge « envoyé à la paie »', () => {
    const { interrupteur, ligne } = monter({ config: config({ domaines: { cantine: true, frais: true, codeCantine: 'CANT01', codeCantineParDefaut: false } }) });
    expect(interrupteur('la cantine').checked).toBe(true);
    expect(interrupteur('les frais de déplacement').checked).toBe(true);
    expect(ligne('Cantine').textContent).toContain('envoyé à la paie');
    expect(ligne('Cantine').textContent).not.toContain('non envoyé');
  });

  it('interrupteur de la cantine refusé tant qu\'aucun code n\'est choisi, avec la raison en infobulle et en texte', () => {
    const { interrupteur, ligne, d } = monter();
    const cantine = interrupteur('la cantine');
    expect(cantine.disabled).toBe(true);
    expect(d.code()).toBe('');
    expect(cantine.closest('label')?.getAttribute('title')).toContain("Choisissez d'abord le code de paie");
    expect(ligne('Cantine').textContent).toContain("Choisissez d'abord le code de paie");
  });

  it('choisir une constante de la liste autorise la cantine ; cocher puis confirmer : un seul appel avec le code', async () => {
    const { d, svc, confirm, toast, interrupteur, fixture } = monter();
    d.choisirCode('CANT01');
    fixture.detectChanges();
    expect(interrupteur('la cantine').disabled).toBe(false);
    interrupteur('la cantine').click();
    fixture.detectChanges();
    expect(d.modifie()).toBe(true);
    await d.enregistrer();
    expect(confirm.ask).toHaveBeenCalledWith(expect.stringContaining('code de constante de la cantine CANT01'), 'Domaines envoyés à la paie', 'Enregistrer');
    expect(confirm.ask).toHaveBeenCalledWith(expect.stringContaining('cantine exigée à la clôture'), 'Domaines envoyés à la paie', 'Enregistrer');
    expect(svc.saveDomains).toHaveBeenCalledTimes(1);
    expect(svc.saveDomains).toHaveBeenCalledWith({ cantine: true, frais: false, codeCantine: 'CANT01' });
    expect(toast.success).toHaveBeenCalledWith('Domaines enregistrés.');
  });

  it('liste vide : message de création dans SAGE, aucune liste, interrupteur de la cantine désactivé avec infobulle', () => {
    const { el, interrupteur, ligne } = monter({ config: config({ codesPaie: [] }) });
    expect(ligne('Cantine').textContent).toContain(MESSAGE_AUCUNE_CONSTANTE);
    expect(ligne('Cantine').querySelector('select')).toBeNull();
    expect(interrupteur('la cantine').disabled).toBe(true);
    expect(interrupteur('la cantine').closest('label')?.getAttribute('title')).toBe(MESSAGE_AUCUNE_CONSTANTE);
    expect(MESSAGE_AUCUNE_CONSTANTE).toBe('Aucune constante dans cette base SAGE : créez d\'abord la constante de la cantine dans SAGE.');
    expect(el.querySelector('tbody')).not.toBeNull();
  });

  it('code enregistré absent de la liste : affiché avec un avertissement, non modifié, cantine toujours éteignable', async () => {
    const { d, ligne, interrupteur, fixture, svc } = monter({
      config: config({ domaines: { cantine: true, frais: false, codeCantine: 'OLD9', codeCantineParDefaut: false } }),
    });
    expect(d.code()).toBe('OLD9');
    expect(d.codeAbsent()).toBe(true);
    expect(d.options()[0]).toEqual({ value: 'OLD9', label: 'OLD9 — introuvable dans SAGE' });
    expect(ligne('Cantine').textContent).toContain("n'existe plus dans SAGE");
    expect(d.modifie()).toBe(false);
    expect(interrupteur('la cantine').disabled).toBe(false);
    interrupteur('la cantine').click();
    fixture.detectChanges();
    await d.enregistrer();
    expect(svc.saveDomains).toHaveBeenCalledWith({ cantine: false, frais: false });
  });

  it('code absent de la liste et cantine éteinte : activation refusée avec la raison', () => {
    const { d, interrupteur } = monter({ config: config({ domaines: { cantine: false, frais: false, codeCantine: 'OLD9', codeCantineParDefaut: false } }) });
    expect(interrupteur('la cantine').disabled).toBe(true);
    expect(d.raisonCantine()).toContain("n'existe plus dans SAGE");
  });

  it('frais : code HA06 affiché en lecture seule avec la mention du code fixe, jamais envoyé', async () => {
    const { ligne, interrupteur, d, svc, fixture } = monter();
    const frais = ligne('Frais de déplacement');
    expect(frais.querySelector('.pv-readonly')?.textContent).toContain('HA06');
    expect(frais.querySelector('.pv-readonly')?.textContent).toContain('code fixe');
    expect(frais.querySelector('select, input[type="text"]')).toBeNull();
    interrupteur('les frais de déplacement').click();
    fixture.detectChanges();
    await d.enregistrer();
    expect(svc.saveDomains).toHaveBeenCalledWith({ cantine: false, frais: true });
  });

  it('rien n\'est enregistré sans confirmation', async () => {
    const { d, svc } = monter({ confirme: false });
    d.frais.set(true);
    await d.enregistrer();
    expect(svc.saveDomains).not.toHaveBeenCalled();
  });

  it('le mois déjà envoyé ne bloque pas les domaines (ils ne changent pas ce qui est envoyé en paie)', () => {
    const { interrupteur } = monter({ config: config({ moisEnvoye: true }) });
    expect(interrupteur('les frais de déplacement').disabled).toBe(false);
  });

  it('le refus du serveur est affiché tel quel (la validation du code reste côté serveur)', async () => {
    const refus = 'Code trop long : 10 caractères au maximum.';
    const { d, fixture, el } = monter({ refus });
    d.choisirCode('CANTINE_C01');
    await d.enregistrer();
    fixture.detectChanges();
    expect(el.querySelector('.pv-banner--danger[role="alert"]')?.textContent?.trim()).toBe(refus);
  });

  it('base AUTRE : aucune ligne Cantine ni Frais, une note explique que la clôture ne les exige pas', () => {
    const { el, ligne } = monter({ config: config({ typeBase: 'AUTRE' }) });
    expect(ligne('Cantine')).toBeUndefined();
    expect(el.querySelector('.pv-domains-save')).toBeNull();
    expect(el.textContent).toContain('la clôture ne les exige pas');
  });

  it('un rechargement des variables garde un brouillon de domaines non enregistré', () => {
    const { d, fixture } = monter();
    d.frais.set(true);
    fixture.componentInstance.charger();
    fixture.detectChanges();
    expect(d.frais()).toBe(true);
  });

  it('la question de confirmation ne cite que ce qui change', () => {
    expect(questionDomaines('B', { cantine: false, frais: true }, { cantine: false, frais: false }))
      .toBe('Enregistrer pour la base B : frais de déplacement non exigés ? Le réglage ne change rien à ce qui est envoyé en paie, '
        + 'seulement ce que la clôture exige.');
  });
});
