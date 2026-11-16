import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ConfirmService } from '../../core/confirm.service';
import { Droit } from '../../core/rights';
import { SessionStore } from '../../core/session.store';
import { ToastService } from '../../core/toast.service';
import { PayrollVariable, PayrollVariablesConfig } from '../../shared/models';
import { PayrollVariablesService } from '../../shared/services/payroll-variables.service';
import { PayrollVariablesCard } from './payroll-variables';
import {
  ACTIVES_PAR_DEFAUT,
  changements,
  colonnesNonEnvoyees,
  libelleColonne,
  peutCocher,
  raisonNonCochable,
  validerBrouillon,
  versEnregistrement,
} from './payroll-variables.logic';

const LIBELLES: Record<string, string> = {
  Exo130: 'EXO 130', Exo150: 'EXO 150', I130: 'IMPO 130', I150: 'IMPO 150', Ferie: 'Férié',
  Dimanche: 'Dimanche', Nuit: 'Nuit', Retard: 'Retard', Absence: 'Absence',
};

function variable(nom: string, champs: Partial<PayrollVariable> = {}): PayrollVariable {
  const sansEnvoi = nom === 'Retard' || nom === 'Absence';
  return {
    variable: nom, libelle: LIBELLES[nom], actif: !sansEnvoi, codePaie: null, unite: null, colonneCible: null,
    cibleExistante: sansEnvoi ? null : 'HS0X', sansEnvoi, ...champs,
  };
}

function config(champs: Partial<PayrollVariablesConfig> = {}): PayrollVariablesConfig {
  return {
    base: 'SAGE_DEMO', typeBase: 'STANDARD', parDefaut: true, moisEnvoye: false, dernierEnvoi: null,
    variables: Object.keys(LIBELLES).map((n) => variable(n)), colonnesCibles: [], ...champs,
  };
}

describe('Variables envoyées à la paie : règles de l\'écran', () => {
  it('Retard et Absence ne sont cochables qu\'avec leur code (SAGE) ou leur colonne (autre base)', () => {
    expect(peutCocher(variable('Retard'), false, false)).toBe(false);
    expect(raisonNonCochable(variable('Retard'), false, false)).toBe('Renseignez d’abord le code de paie.');
    expect(peutCocher(variable('Retard', { codePaie: 'RET01' }), false, false)).toBe(true);
    expect(peutCocher(variable('Absence', { codePaie: 'ABS01' }), true, false)).toBe(false);
    expect(raisonNonCochable(variable('Absence'), true, false)).toBe('Renseignez d’abord la colonne cible.');
    expect(peutCocher(variable('Absence', { colonneCible: 'JoursAbsence' }), true, false)).toBe(true);
    expect(peutCocher(variable('Nuit'), false, false)).toBe(true);
  });

  it('mois déjà envoyé : aucune case n\'est modifiable, avec la raison affichée', () => {
    expect(peutCocher(variable('Nuit'), false, true)).toBe(false);
    expect(raisonNonCochable(variable('Nuit'), false, true)).toBe('Mois déjà envoyé : modification impossible.');
  });

  it('validation identique au serveur : cible et unité exigées pour une variable sans envoi cochée', () => {
    const erreurs = validerBrouillon([variable('Retard', { actif: true }), variable('Absence', { actif: true, codePaie: 'ABS', unite: 'J' })], false);
    expect(erreurs).toEqual([
      'La variable Retard ne peut pas être cochée tant que son code de paie n\'est pas renseigné.',
      'La variable Retard ne peut pas être cochée sans unité (heures ou jours).',
    ]);
    expect(validerBrouillon(config().variables, false)).toEqual([]);
  });

  it('changements : seuls les écarts avec la configuration lue sont listés, champs vides comparés comme absents', () => {
    const initiales = config().variables;
    const brouillon = initiales.map((v) => (v.variable === 'Nuit' ? { ...v, actif: false } : v.variable === 'Retard' ? { ...v, codePaie: '  ' } : v));
    expect(changements(initiales, brouillon)).toEqual(['Nuit : non envoyée à la paie']);
    expect(changements(initiales, initiales)).toEqual([]);
  });

  it('enregistrement : neuf lignes, textes vides ramenés à null', () => {
    const lignes = versEnregistrement(config({ variables: [variable('Retard', { codePaie: ' ', unite: 'H' })] }).variables);
    expect(lignes).toEqual([{ variable: 'Retard', actif: false, codePaie: null, unite: 'H', colonneCible: null }]);
  });
});

describe('Page Heures supplémentaires : colonnes non envoyées', () => {
  it('par défaut, seules Retard et Absence portent la mention ; aucune colonne n\'est retirée', () => {
    const set = colonnesNonEnvoyees(ACTIVES_PAR_DEFAUT);
    expect([...set].sort()).toEqual(['absence', 'retard']);
    expect(libelleColonne('Nuit', true)).toBe('Nuit');
    expect(libelleColonne('Retard', false)).toBe('Retard (non envoyée à la paie)');
  });

  it('variable décochée : sa colonne est grisée', () => {
    const set = colonnesNonEnvoyees(['Exo130', 'Exo150', 'I130', 'I150', 'Ferie', 'Nuit']);
    expect(set.has('dim')).toBe(true);
    expect(set.has('nuit')).toBe(false);
  });
});

/** Carte sans rendu : décisions de droit, confirmation et appels au service. */
function monter(options: { permissions?: string[]; base?: string | null; reponse?: PayrollVariablesConfig; confirme?: boolean } = {}) {
  TestBed.resetTestingModule();
  const svc = {
    get: vi.fn(() => of(options.reponse ?? config())),
    save: vi.fn(() => of(config({ parDefaut: false }))),
  };
  const toast = { error: vi.fn(), success: vi.fn() };
  const confirm = { ask: vi.fn(async (_message: string, _titre?: string, _ok?: string) => options.confirme ?? true) };
  TestBed.configureTestingModule({
    providers: [
      { provide: PayrollVariablesService, useValue: svc },
      {
        provide: SessionStore,
        useValue: {
          activeSageDb: () => (options.base === undefined ? 'SAGE_DEMO' : options.base),
          permissions: () => options.permissions ?? ['SAGE:SAGE_DEMO:UPDATE'],
          hasRight: (d: number) => d === Droit.Databases,
        },
      },
      { provide: ToastService, useValue: toast },
      { provide: ConfirmService, useValue: confirm },
    ],
  });
  const carte = TestBed.runInInjectionContext(() => new PayrollVariablesCard());
  carte.ngOnInit();
  return { carte, svc, toast, confirm };
}

describe('Carte « Variables envoyées à la paie »', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('sans la permission de modifier la base active : rien n\'est demandé au serveur', () => {
    const { carte, svc } = monter({ permissions: ['SAGE:AUTRE:UPDATE'] });
    expect(carte.autorise()).toBe(false);
    expect(svc.get).not.toHaveBeenCalled();
  });

  it('sans base active : rien n\'est demandé au serveur', () => {
    const { svc } = monter({ base: null });
    expect(svc.get).not.toHaveBeenCalled();
  });

  it('aucun envoi tant que l\'utilisateur n\'a pas confirmé', async () => {
    const { carte, svc, confirm } = monter({ confirme: false });
    carte.basculer('Nuit', false);
    expect(carte.modifie()).toBe(true);

    await carte.enregistrer();

    expect(confirm.ask).toHaveBeenCalledTimes(1);
    expect(confirm.ask.mock.calls[0][0]).toContain('Nuit : non envoyée à la paie');
    expect(svc.save).not.toHaveBeenCalled();
  });

  it('confirmé : les neuf variables partent, le brouillon est remplacé par la réponse', async () => {
    const { carte, svc, toast } = monter();
    carte.basculer('Nuit', false);

    await carte.enregistrer();

    expect(svc.save).toHaveBeenCalledTimes(1);
    const envoyees = (svc.save.mock.calls[0] as unknown[])[0] as { variable: string; actif: boolean }[];
    expect(envoyees).toHaveLength(9);
    expect(envoyees.find((v) => v.variable === 'Nuit')?.actif).toBe(false);
    expect(toast.success).toHaveBeenCalled();
    expect(carte.modifie()).toBe(false);
  });

  it('erreur de validation : message durable, aucune confirmation ni requête', async () => {
    const { carte, svc, confirm } = monter();
    carte.changer('Retard', 'codePaie', 'RET01');
    carte.basculer('Retard', true);

    await carte.enregistrer();

    expect(carte.erreurs()).toEqual(['La variable Retard ne peut pas être cochée sans unité (heures ou jours).']);
    expect(confirm.ask).not.toHaveBeenCalled();
    expect(svc.save).not.toHaveBeenCalled();
  });

  it('refus du serveur (mois envoyé entre-temps) : le message reste affiché', async () => {
    const { carte, svc } = monter();
    svc.save.mockReturnValueOnce(throwError(() => ({ status: 409, error: { error: 'Mois déjà envoyé en paie (période 2026-11-01 → 2026-11-30) : modification impossible.' } })));
    carte.basculer('Nuit', false);

    await carte.enregistrer();

    expect(carte.erreurs()[0]).toContain('Mois déjà envoyé en paie');
    expect(carte.modifie()).toBe(true);
  });

  it('effacer le code de Retard décoche la variable', () => {
    const { carte } = monter();
    carte.changer('Retard', 'codePaie', 'RET01');
    carte.changer('Retard', 'unite', 'H');
    carte.basculer('Retard', true);
    carte.changer('Retard', 'codePaie', '');
    expect(carte.brouillon().find((v) => v.variable === 'Retard')?.actif).toBe(false);
  });

  it('mois déjà envoyé : enregistrer ne fait rien', async () => {
    const { carte, svc, confirm } = monter({ reponse: config({ moisEnvoye: true, dernierEnvoi: '2026-11-01 → 2026-11-30' }) });
    carte.basculer('Nuit', false);
    await carte.enregistrer();
    expect(confirm.ask).not.toHaveBeenCalled();
    expect(svc.save).not.toHaveBeenCalled();
  });
});
