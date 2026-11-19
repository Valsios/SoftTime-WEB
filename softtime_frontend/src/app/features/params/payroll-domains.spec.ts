import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ConfirmService } from '../../core/confirm.service';
import { ToastService } from '../../core/toast.service';
import { PayrollVariablesConfig } from '../../shared/models';
import { PayrollVariablesService } from '../../shared/services/payroll-variables.service';
import { PayrollDomainsSetting, questionDomaines } from './payroll-domains';

function config(champs: Partial<PayrollVariablesConfig> = {}): PayrollVariablesConfig {
  return {
    base: 'SAGE_DEMO', typeBase: 'STANDARD', parDefaut: false, moisEnvoye: true, dernierEnvoi: '2026-11-01 → 2026-11-30',
    variables: [], colonnesCibles: [], domaines: { cantine: false, frais: false, codeCantine: 'CANTINE_CODE', codeCantineParDefaut: true }, ...champs,
  };
}

function monter(options: { config?: PayrollVariablesConfig; confirme?: boolean; refus?: string } = {}) {
  const svc = {
    saveDomains: vi.fn(() => (options.refus
      ? throwError(() => ({ status: 400, error: { message: options.refus } }))
      : of(config({ domaines: { cantine: true, frais: false } })))),
  };
  const toast = { success: vi.fn(), error: vi.fn(), info: vi.fn(), warning: vi.fn() };
  const confirm = { ask: vi.fn(async () => options.confirme ?? true) };
  TestBed.configureTestingModule({
    imports: [PayrollDomainsSetting],
    providers: [
      { provide: PayrollVariablesService, useValue: svc },
      { provide: ToastService, useValue: toast },
      { provide: ConfirmService, useValue: confirm },
    ],
  });
  const fixture = TestBed.createComponent(PayrollDomainsSetting);
  fixture.componentRef.setInput('config', options.config ?? config());
  fixture.detectChanges();
  const el = fixture.nativeElement as HTMLElement;
  const cases = () => [...el.querySelectorAll<HTMLInputElement>('input[type="checkbox"]')];
  const bouton = () => [...el.querySelectorAll('button')].find((b) => b.textContent?.includes('Enregistrer les domaines'));
  return { fixture, comp: fixture.componentInstance, svc, toast, confirm, el, cases, bouton };
}

describe('Domaines envoyés à la paie (cantine, frais)', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('désactivés par défaut, cases décochées et enregistrement inactif tant que rien ne change', () => {
    const { cases, bouton } = monter({ config: config({ domaines: null }) });
    expect(cases().map((c) => c.checked)).toEqual([false, false]);
    expect(bouton()?.disabled).toBe(true);
  });

  it('reprend le réglage enregistré', () => {
    const { cases } = monter({ config: config({ domaines: { cantine: true, frais: true } }) });
    expect(cases().map((c) => c.checked)).toEqual([true, true]);
  });

  it('cocher la cantine puis confirmer : un seul appel, avec le réglage complet', async () => {
    const { comp, svc, confirm, toast, cases, fixture } = monter();
    cases()[0].click();
    fixture.detectChanges();
    expect(comp.modifie()).toBe(true);
    await comp.enregistrer();
    expect(confirm.ask).toHaveBeenCalledWith(expect.stringContaining('cantine exigée à la clôture'), 'Domaines envoyés à la paie', 'Enregistrer');
    expect(svc.saveDomains).toHaveBeenCalledWith({ cantine: true, frais: false });
    expect(toast.success).toHaveBeenCalledWith('Domaines enregistrés.');
  });

  it('rien n\'est enregistré sans confirmation', async () => {
    const { comp, svc } = monter({ confirme: false });
    comp.frais.set(true);
    await comp.enregistrer();
    expect(svc.saveDomains).not.toHaveBeenCalled();
  });

  it('le mois déjà envoyé ne bloque pas le réglage (il ne change pas ce qui est envoyé en paie)', () => {
    const { cases } = monter({ config: config({ moisEnvoye: true }) });
    expect(cases().every((c) => !c.disabled)).toBe(true);
  });

  it('le refus du serveur est affiché tel quel', async () => {
    const refus = "La cantine et les frais de déplacement ne s'envoient qu'à une base SAGE STANDARD : ils ne peuvent pas être activés pour une base AUTRE.";
    const { comp, fixture, el } = monter({ refus });
    comp.cantine.set(true);
    await comp.enregistrer();
    fixture.detectChanges();
    expect(el.querySelector('[role="alert"]')?.textContent?.trim()).toBe(refus);
  });

  it('base AUTRE : aucune case, une note explique que les domaines ne sont pas exigés', () => {
    const { cases, el } = monter({ config: config({ typeBase: 'AUTRE' }) });
    expect(cases()).toHaveLength(0);
    expect(el.textContent).toContain('la clôture ne les exige pas');
  });

  it('la question de confirmation ne cite que ce qui change', () => {
    expect(questionDomaines('B', { cantine: false, frais: true }, { cantine: false, frais: false }))
      .toBe('Enregistrer pour la base B : frais de déplacement non exigés ? Le réglage ne change rien à ce qui est envoyé en paie, '
        + 'seulement ce que la clôture exige.');
  });

  it('vue étroite : les cases passent en colonne sous 768 px', () => {
    const styles = (PayrollDomainsSetting as unknown as { ɵcmp: { styles: string[] } }).ɵcmp.styles.join('');
    expect(styles).toContain('max-width: 767px');
  });

  it('champ « Code de constante de la cantine » : code utilisé affiché, avertissement du code historique', () => {
    const { el } = monter();
    const champ = el.querySelector<HTMLInputElement>('.pd__code input')!;
    expect(champ.value).toBe('CANTINE_CODE');
    expect(el.querySelector('.pd__code')?.textContent).toContain('Code de constante de la cantine');
    expect(el.querySelector('.pd__code')?.textContent).toContain("le code historique CANTINE_CODE n'existe dans aucune base SAGE");
  });

  it('code saisi : envoyé (sans espaces) avec le réglage, et cité dans la confirmation', async () => {
    const { comp, svc, confirm, el, fixture } = monter();
    const champ = el.querySelector<HTMLInputElement>('.pd__code input')!;
    champ.value = ' CANT01 ';
    champ.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect(comp.modifie()).toBe(true);
    await comp.enregistrer();
    expect(confirm.ask).toHaveBeenCalledWith(expect.stringContaining('code de constante de la cantine CANT01'), 'Domaines envoyés à la paie', 'Enregistrer');
    expect(svc.saveDomains).toHaveBeenCalledWith({ cantine: false, frais: false, codeCantine: 'CANT01' });
  });

  it("code inchangé : non envoyé (le serveur le garde), pas d'avertissement pour un code configuré", async () => {
    const { comp, svc, el } = monter({ config: config({ domaines: { cantine: true, frais: false, codeCantine: 'CANT01', codeCantineParDefaut: false } }) });
    expect(el.querySelector('.pd__code')?.textContent).not.toContain('code historique');
    comp.frais.set(true);
    await comp.enregistrer();
    expect(svc.saveDomains).toHaveBeenCalledWith({ cantine: true, frais: true });
  });

  it('refus du code par le serveur affiché tel quel', async () => {
    const refus = 'Code trop long : 10 caractères au maximum.';
    const { comp, fixture, el } = monter({ refus });
    comp.code.set('CANTINE_C01');
    await comp.enregistrer();
    fixture.detectChanges();
    expect(el.querySelector('[role="alert"]')?.textContent?.trim()).toBe(refus);
  });

  it('base AUTRE : pas de champ de code', () => {
    const { el } = monter({ config: config({ typeBase: 'AUTRE' }) });
    expect(el.querySelector('.pd__code')).toBeNull();
  });
});
