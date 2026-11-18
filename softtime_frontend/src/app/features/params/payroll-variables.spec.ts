import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ConfirmService } from '../../core/confirm.service';
import { Droit } from '../../core/rights';
import { SessionStore } from '../../core/session.store';
import { ToastService } from '../../core/toast.service';
import { PayrollAbsenceCode, PayrollTargetColumn, PayrollVariable, PayrollVariablesConfig, PayrollWriteTarget, PayrollWriteTargets } from '../../shared/models';
import { PayrollVariablesService } from '../../shared/services/payroll-variables.service';
import { PayrollDestinations } from './payroll-destinations';
import { PayrollVariablesCard } from './payroll-variables';
import {
  ACTIVES_PAR_DEFAUT,
  changementOption,
  changements,
  changementHeuresParJour,
  changementsCodesAbsence,
  codesAbsenceLus,
  erreurHeuresParJour,
  heuresParJourValide,
  texteHeures,
  changementsDestinations,
  choixColonnes,
  colonnesNonEnvoyees,
  validerDestinations,
  versDestinations,
  libelleColonne,
  peutCocher,
  raisonNonCochable,
  questionEnvoi,
  validerBrouillon,
  versEnregistrement,
} from './payroll-variables.logic';

const LIBELLES: Record<string, string> = {
  Exo130: 'EXO 130', Exo150: 'EXO 150', I130: 'IMPO 130', I150: 'IMPO 150', Ferie: 'Férié',
  Dimanche: 'Dimanche', Nuit: 'Nuit', Retard: 'Retard', Absence: 'Absence',
};

function variable(nom: string, champs: Partial<PayrollVariable> = {}): PayrollVariable {
  const codePropre = nom === 'Retard' || nom === 'Absence';
  return {
    variable: nom, libelle: LIBELLES[nom], actif: !codePropre, codePaie: null, unite: null, colonneCible: null,
    cibleExistante: codePropre ? null : 'HS0X', sansEnvoi: false, ...champs,
  };
}

function config(champs: Partial<PayrollVariablesConfig> = {}): PayrollVariablesConfig {
  return {
    base: 'SAGE_DEMO', typeBase: 'STANDARD', parDefaut: true, moisEnvoye: false, dernierEnvoi: null,
    variables: Object.keys(LIBELLES).map((n) => variable(n)), colonnesCibles: [], ...champs,
  };
}

describe('Variables envoyées à la paie : règles de l\'écran', () => {
  // Adapté au lot C2 (avant : Absence cochable avec sa colonne ; messages « Renseignez d'abord … »).
  it('Retard n\'est cochable qu\'avec son code (SAGE) ou sa destination (autre base)', () => {
    expect(peutCocher(variable('Retard'), false, false)).toBe(false);
    expect(raisonNonCochable(variable('Retard'), false, false)).toBe('Choisissez d’abord le code de paie.');
    expect(peutCocher(variable('Retard', { codePaie: 'RET01' }), false, false)).toBe(true);
    expect(peutCocher(variable('Retard', { colonneCible: 'X' }), true, false)).toBe(false);
    expect(raisonNonCochable(variable('Retard'), true, false)).toBe('Choisissez d’abord sa colonne dans « Destinations dans la base de paie ».');
    expect(peutCocher(variable('Retard', { cibleExistante: 'HeuresRetard' }), true, false)).toBe(true);
    expect(peutCocher(variable('Nuit'), false, false)).toBe(true);
  });

  // Adapté au lot C3 (avant : Absence jamais cochable, « prochaine étape ») : comme Retard.
  it('Absence n\'est cochable qu\'avec son code (SAGE) ou sa destination (autre base), comme Retard', () => {
    expect(peutCocher(variable('Absence'), false, false)).toBe(false);
    expect(raisonNonCochable(variable('Absence'), false, false)).toBe('Choisissez d’abord le code de paie.');
    expect(peutCocher(variable('Absence', { codePaie: 'ABS01', unite: 'J' }), false, false)).toBe(true);
    expect(peutCocher(variable('Absence', { colonneCible: 'JoursAbsence' }), true, false)).toBe(false);
    expect(peutCocher(variable('Absence', { cibleExistante: 'JoursAbsence' }), true, false)).toBe(true);
    expect(peutCocher(variable('Absence', { actif: true }), false, false)).toBe(true);
  });

  it('mois déjà envoyé : aucune case n\'est modifiable, avec la raison affichée', () => {
    expect(peutCocher(variable('Nuit'), false, true)).toBe(false);
    expect(raisonNonCochable(variable('Nuit'), false, true)).toBe('Mois déjà envoyé : modification impossible.');
  });

  // Adapté au lot C2 (avant : unité de Retard exigée, Absence cochée acceptée) puis au lot C3 (Absence cochée avec code et
  // unité acceptée ; sans unité, refusée).
  it('validation identique au serveur : code de Retard exigé (unité heures par défaut), unité d\'Absence exigée', () => {
    const erreurs = validerBrouillon([variable('Retard', { actif: true }), variable('Absence', { actif: true, codePaie: 'ABS' })], false);
    expect(erreurs).toEqual([
      'La variable Retard ne peut pas être cochée tant que son code de paie n\'est pas renseigné.',
      'La variable Absence ne peut pas être cochée sans unité (heures ou jours).',
    ]);
    expect(validerBrouillon([variable('Absence', { actif: true, codePaie: 'ABS', unite: 'J' })], false)).toEqual([]);
    expect(validerBrouillon([variable('Retard', { actif: true, codePaie: 'RET01', unite: 'J' })], false))
      .toEqual(['L\'unité de la variable Retard doit être « heures ».']);
    expect(validerBrouillon([variable('Retard', { actif: true, cibleExistante: 'HeuresRetard' })], true)).toEqual([]);
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
    expect(versEnregistrement([variable('Retard', { codePaie: 'RET01' })])[0].unite).toBe('H');
  });
});

describe('Destinations d\'une autre base : règles de l\'écran', () => {
  const colonnes: PayrollTargetColumn[] = [
    { nom: 'Matricule', type: 'nvarchar', numerique: false, accepteDecimales: false },
    { nom: 'HeuresRetard', type: 'decimal(18,2)', numerique: true, accepteDecimales: true },
    { nom: 'Entier', type: 'int', numerique: true, accepteDecimales: false },
  ];
  const cible = (nom: string, colonne: string | null, actif = true): PayrollWriteTarget =>
    ({ variable: nom, libelle: LIBELLES[nom], actif, colonne, decimalesRequises: nom === 'Retard' ? 2 : 0 });

  it('colonnes de Retard : non numériques et sans décimales désactivées, valeur introuvable conservée', () => {
    expect(choixColonnes(colonnes, 2, null)).toEqual([
      { nom: 'Matricule', libelle: 'Matricule (nvarchar, non numérique)', utilisable: false },
      { nom: 'HeuresRetard', libelle: 'HeuresRetard (decimal(18,2))', utilisable: true },
      { nom: 'Entier', libelle: 'Entier (int, sans décimales)', utilisable: false },
    ]);
    expect(choixColonnes(colonnes, 0, null).find((c) => c.nom === 'Entier')?.utilisable).toBe(true);
    expect(choixColonnes(colonnes, 2, 'Ancienne')[0]).toEqual({ nom: 'Ancienne', libelle: 'Ancienne (introuvable dans la table)', utilisable: false });
  });

  it('validation : table, matricule, colonne d\'une variable envoyée, colonne en double', () => {
    expect(validerDestinations({ table: null, colonneMatricule: null, variables: [cible('Nuit', null)] })).toEqual([
      'Choisissez la table de destination.',
      'Choisissez la colonne matricule.',
      'La variable Nuit est envoyée à la paie : choisissez sa colonne de destination.',
    ]);
    expect(validerDestinations({ table: 'T', colonneMatricule: 'M', variables: [cible('Nuit', 'H'), cible('Retard', 'h', false)] }))
      .toEqual(['La colonne H est choisie pour plusieurs variables (Nuit, Retard).']);
    expect(validerDestinations({ table: 'T', colonneMatricule: 'M', variables: [cible('Retard', null, false)] })).toEqual([]);
  });

  it('changements et enregistrement : écarts lisibles, champs vides ramenés à null', () => {
    const avant = { table: 'T', colonneMatricule: 'M', variables: [cible('Retard', null, false)] };
    const apres = { ...avant, variables: [cible('Retard', 'HeuresRetard', false)] };
    expect(changementsDestinations(avant, apres)).toEqual(['Retard : colonne HeuresRetard']);
    expect(changementsDestinations(avant, avant)).toEqual([]);
    expect(versDestinations({ table: ' T ', colonneMatricule: '', variables: [cible('Retard', ' ', false)] }))
      .toEqual({ table: 'T', colonneMatricule: null, variables: [{ variable: 'Retard', colonne: null }] });
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

  // Adapté au lot C2 (avant : unité de Retard manquante ; l'unité est désormais toujours « heures »).
  it('erreur de validation : message durable, aucune confirmation ni requête', async () => {
    const { carte, svc, confirm } = monter();
    carte.basculer('Retard', true);

    await carte.enregistrer();

    expect(carte.erreurs()).toEqual(['La variable Retard ne peut pas être cochée tant que son code de paie n\'est pas renseigné.']);
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

  it('Retard avec un code de la liste SAGE : confirmé, la ligne part avec le code et l\'unité heures', async () => {
    const { carte, svc } = monter({ reponse: config({ codesPaie: [{ code: 'RET01', intitule: 'Retard' }] }) });
    carte.changer('Retard', 'codePaie', 'RET01');
    carte.basculer('Retard', true);

    await carte.enregistrer();

    const envoyees = (svc.save.mock.calls[0] as unknown[])[0] as { variable: string; actif: boolean; codePaie: string; unite: string }[];
    expect(envoyees.find((v) => v.variable === 'Retard')).toMatchObject({ actif: true, codePaie: 'RET01', unite: 'H' });
  });

  it('liste des codes : un code enregistré absent de SAGE reste visible et signalé', () => {
    const { carte } = monter({ reponse: config({ codesPaie: [{ code: 'RET01', intitule: 'Retard' }] }) });
    expect(carte.codes(variable('Retard', { codePaie: 'OLD9' })).map((o) => o.code)).toEqual(['OLD9', 'RET01']);
    expect(carte.codes(variable('Retard', { codePaie: 'ret01' })).map((o) => o.code)).toEqual(['RET01']);
  });

  it('mois déjà envoyé : enregistrer ne fait rien', async () => {
    const { carte, svc, confirm } = monter({ reponse: config({ moisEnvoye: true, dernierEnvoi: '2026-11-01 → 2026-11-30' }) });
    carte.basculer('Nuit', false);
    await carte.enregistrer();
    expect(confirm.ask).not.toHaveBeenCalled();
    expect(svc.save).not.toHaveBeenCalled();
  });
});

/** Section des destinations sans rendu : confirmation, changement de table, enregistrement. */
function monterDestinations(options: { confirme?: boolean; reponse?: Partial<PayrollWriteTargets> } = {}) {
  TestBed.resetTestingModule();
  const lues: PayrollWriteTargets = {
    base: 'SAGE_AUTRE', table: 'PAIE_CUMULS', colonneMatricule: 'Matricule',
    variables: [
      { variable: 'Nuit', libelle: 'Nuit', actif: true, colonne: 'HeuresNuit', decimalesRequises: 0 },
      { variable: 'Retard', libelle: 'Retard', actif: false, colonne: null, decimalesRequises: 2 },
    ],
    tables: ['PAIE_CUMULS', 'AUTRE_TABLE'],
    colonnes: [
      { nom: 'Matricule', type: 'nvarchar', numerique: false, accepteDecimales: false },
      { nom: 'HeuresNuit', type: 'decimal(18,2)', numerique: true, accepteDecimales: true },
      { nom: 'HeuresRetard', type: 'decimal(18,2)', numerique: true, accepteDecimales: true },
    ],
    moisEnvoye: false, dernierEnvoi: null, ...options.reponse,
  };
  const svc = {
    destinations: vi.fn(() => of(lues)),
    colonnes: vi.fn(() => of([{ nom: 'Mat', type: 'int', numerique: true, accepteDecimales: false }])),
    saveDestinations: vi.fn(() => of(lues)),
  };
  const toast = { error: vi.fn(), success: vi.fn() };
  const confirm = { ask: vi.fn(async (_message: string, _titre?: string, _ok?: string) => options.confirme ?? true) };
  TestBed.configureTestingModule({
    providers: [
      { provide: PayrollVariablesService, useValue: svc },
      { provide: ToastService, useValue: toast },
      { provide: ConfirmService, useValue: confirm },
    ],
  });
  const section = TestBed.runInInjectionContext(() => new PayrollDestinations());
  section.ngOnInit();
  return { section, svc, confirm };
}

describe('Destinations dans la base de paie (autre base)', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('aucun envoi tant que l\'utilisateur n\'a pas confirmé', async () => {
    const { section, svc, confirm } = monterDestinations({ confirme: false });
    section.choisirColonne('Retard', 'HeuresRetard');

    await section.enregistrer();

    expect(confirm.ask.mock.calls[0][0]).toContain('Retard : colonne HeuresRetard');
    expect(svc.saveDestinations).not.toHaveBeenCalled();
  });

  it('confirmé : la destination de Retard part, la carte est prévenue', async () => {
    const { section, svc } = monterDestinations();
    let prevenue = false;
    section.enregistre.subscribe(() => (prevenue = true));
    section.choisirColonne('Retard', 'HeuresRetard');

    await section.enregistrer();

    expect(svc.saveDestinations).toHaveBeenCalledWith({
      table: 'PAIE_CUMULS', colonneMatricule: 'Matricule',
      variables: [{ variable: 'Nuit', colonne: 'HeuresNuit' }, { variable: 'Retard', colonne: 'HeuresRetard' }],
    });
    expect(prevenue).toBe(true);
  });

  it('nouvelle table : matricule et colonnes remis à choisir, colonnes de la table lues', () => {
    const { section, svc } = monterDestinations();

    section.choisirTable('AUTRE_TABLE');

    expect(svc.colonnes).toHaveBeenCalledWith('AUTRE_TABLE');
    expect(section.brouillon()).toMatchObject({ table: 'AUTRE_TABLE', colonneMatricule: null });
    expect(section.brouillon()!.variables.every((v) => v.colonne === null)).toBe(true);
    expect(section.colonnes().map((c) => c.nom)).toEqual(['Mat']);
  });

  it('variable envoyée sans colonne : message durable, aucune confirmation', async () => {
    const { section, svc, confirm } = monterDestinations();
    section.choisirColonne('Nuit', '');

    await section.enregistrer();

    expect(section.erreurs()).toEqual(['La variable Nuit est envoyée à la paie : choisissez sa colonne de destination.']);
    expect(confirm.ask).not.toHaveBeenCalled();
    expect(svc.saveDestinations).not.toHaveBeenCalled();
  });

  it('mois déjà envoyé : enregistrer ne fait rien', async () => {
    const { section, svc, confirm } = monterDestinations({ reponse: { moisEnvoye: true, dernierEnvoi: '2026-11-01 → 2026-11-30' } });
    section.choisirColonne('Retard', 'HeuresRetard');

    await section.enregistrer();

    expect(section.bloque()).toBe(true);
    expect(confirm.ask).not.toHaveBeenCalled();
    expect(svc.saveDestinations).not.toHaveBeenCalled();
  });
});

/** Point 2 (essai à l'écran des lots C1/C2) : le code d'une variable décochée n'est jamais effacé par la carte. */
describe('Carte « Variables envoyées à la paie » : décocher Retard conserve son code', () => {
  afterEach(() => TestBed.resetTestingModule());

  const CODES = [{ code: 'HS01', intitule: 'HS 130' }, { code: 'RET01', intitule: 'Retard' }];
  const retardActif = () => config({
    parDefaut: false,
    codesPaie: CODES,
    variables: Object.keys(LIBELLES).map((n) => n === 'Retard' ? variable(n, { actif: true, codePaie: 'RET01', unite: 'H' }) : variable(n)),
  });
  const retardDecoche = () => config({
    parDefaut: false,
    codesPaie: CODES,
    variables: Object.keys(LIBELLES).map((n) => n === 'Retard' ? variable(n, { actif: false, codePaie: 'RET01', unite: 'H' }) : variable(n)),
  });

  it('décochage, enregistrement, rechargement : le PUT porte RET01 et le brouillon le garde', async () => {
    const { carte, svc } = monter({ reponse: retardActif() });
    svc.save.mockReturnValueOnce(of(retardDecoche()));

    carte.basculer('Retard', false);
    await carte.enregistrer();

    const envoyees = (svc.save.mock.calls[0] as unknown[])[0] as { variable: string; actif: boolean; codePaie: string | null }[];
    expect(envoyees.find((v) => v.variable === 'Retard')).toMatchObject({ actif: false, codePaie: 'RET01' });
    expect(carte.brouillon().find((v) => v.variable === 'Retard')).toMatchObject({ actif: false, codePaie: 'RET01' });

    svc.get.mockReturnValueOnce(of(retardDecoche()));
    carte.charger();
    expect(carte.brouillon().find((v) => v.variable === 'Retard')).toMatchObject({ actif: false, codePaie: 'RET01' });
  });

  it('la liste déroulante montre RET01 sélectionné, avant et après l\'enregistrement du décochage', async () => {
    const { carte, svc } = monter({ reponse: retardActif() });
    svc.save.mockReturnValueOnce(of(retardDecoche()));
    const fixture = TestBed.createComponent(PayrollVariablesCard);
    fixture.componentInstance.ngOnInit();
    fixture.detectChanges();
    const selection = () => (fixture.nativeElement as HTMLElement)
      .querySelector<HTMLSelectElement>('select[aria-label="Code de paie de Retard"]')!;

    expect(selection().value).toBe('RET01');

    fixture.componentInstance.basculer('Retard', false);
    await fixture.componentInstance.enregistrer();
    fixture.detectChanges();

    expect(selection().value).toBe('RET01');
    expect(carte).toBeTruthy();
  });
});

/** Demande 2 : états désactivé et verrouillé de la carte (rendu réel, composants de la charte). */
describe('Carte « Variables envoyées à la paie » : états désactivé et verrouillé', () => {
  afterEach(() => TestBed.resetTestingModule());

  function rendre(reponse: PayrollVariablesConfig, permissions = ['SAGE:SAGE_DEMO:UPDATE']) {
    monter({ reponse, permissions });
    const fixture = TestBed.createComponent(PayrollVariablesCard);
    fixture.componentInstance.ngOnInit();
    fixture.detectChanges();
    const racine = fixture.nativeElement as HTMLElement;
    return { fixture, racine, champ: (libelle: string) => racine.querySelector<HTMLInputElement | HTMLSelectElement>(`[aria-label="${libelle}"]`)! };
  }

  it('mois déjà envoyé : bandeau d\'avertissement, listes, champ de code et cases désactivés, boutons absents', async () => {
    const { racine, champ, fixture } = rendre(config({ moisEnvoye: true, dernierEnvoi: '2026-11-01 → 2026-11-30', codesPaie: [{ code: 'RET01', intitule: 'Retard' }] }));
    // La liste avec recherche (Absence) reçoit son état désactivé de ngModel, de façon asynchrone.
    await fixture.whenStable();
    fixture.detectChanges();

    expect(racine.querySelector('.pv-banner--warning')?.textContent).toContain('Mois déjà envoyé en paie');
    expect(champ('Code de paie de Retard').disabled).toBe(true);
    expect(champ('Code de paie de Absence').disabled).toBe(true);
    expect(champ('Unité de Absence').disabled).toBe(true);
    expect(champ('Envoyer Nuit à la paie').disabled).toBe(true);
    expect(racine.querySelector('.pv-actions')).toBeNull();
  });

  // Adapté au lot C3 (avant : case toujours désactivée, « prochaine étape », champ de code libre).
  it('Absence sans code : case désactivée avec sa raison, liste des codes avec recherche au style de l\'application', () => {
    const { racine, champ } = rendre(config());

    expect(champ('Envoyer Absence à la paie').disabled).toBe(true);
    expect(racine.textContent).not.toContain('prochaine étape');
    expect(champ('Code de paie de Absence').getAttribute('role')).toBe('combobox');
    expect(champ('Code de paie de Absence').classList.contains('soft-input')).toBe(true);
    expect(champ('Code de paie de Absence').disabled).toBe(false);
  });

  it('sans permission : message de permission, aucun tableau ni requête', () => {
    const { racine } = rendre(config(), ['SAGE:AUTRE:UPDATE']);

    expect(racine.textContent).toContain('Vous n\'avez pas la permission de modifier la base SAGE_DEMO');
    expect(racine.querySelector('table')).toBeNull();
  });

  it('les listes de la carte utilisent les composants de la charte', () => {
    const { racine } = rendre(config());

    expect(racine.querySelectorAll('.soft-select select').length).toBeGreaterThan(0);
    expect(racine.querySelector('table.soft-table')).not.toBeNull();
    expect(racine.querySelector('.soft-toggle input[type="checkbox"]')).not.toBeNull();
    expect(racine.querySelectorAll('select:not(.soft-select select)').length).toBe(0);
  });
});

describe('Largeur étroite (< 768 px) : variables présentées en cartes, jamais masquées', () => {
  afterEach(() => TestBed.resetTestingModule());

  /** Styles du composant injectés par Angular (encapsulation émulée). */
  function stylesInjectes(): string {
    return [...document.head.querySelectorAll('style')].map((s) => s.textContent ?? '').join('\n').replace(/\s+/g, ' ');
  }

  /** La règle globale masque .soft-table-wrap sous 768 px : le composant doit la contredire pour son propre tableau. */
  function reaffiche(classe: string): boolean {
    const css = stylesInjectes();
    const media = css.split(/@media/).filter((bloc) => /\(max-width: ?767px\)/.test(bloc));
    const selecteur = `.soft-table-wrap.${classe}`;
    return media.some((bloc) => {
      const i = bloc.indexOf(selecteur);
      return i >= 0 && /^(\[[^\]]+\])? ?\{ ?display: ?block/.test(bloc.slice(i + selecteur.length));
    });
  }

  function rendreCarte(reponse: PayrollVariablesConfig) {
    monter({ reponse });
    const fixture = TestBed.createComponent(PayrollVariablesCard);
    fixture.componentInstance.ngOnInit();
    fixture.detectChanges();
    return { fixture, racine: fixture.nativeElement as HTMLElement, carte: fixture.componentInstance };
  }

  it('carte : le tableau est réaffiché sous 768 px et chaque variable a ses quatre éléments étiquetés', () => {
    const { racine } = rendreCarte(config({ codesPaie: [{ code: 'RET01', intitule: 'Retard' }] }));

    expect(racine.querySelector('.soft-table-wrap')?.classList.contains('pv-wrap')).toBe(true);
    expect(reaffiche('pv-wrap')).toBe(true);
    // Adapté au lot C3 : la liste des codes d'absence comptés forme une ligne à part, sous l'Absence.
    const lignes = [...racine.querySelectorAll('tbody tr:not(.pv-codes-row)')];
    // Adapté aux correctifs C3 : le nom est dans son propre élément, la pastille « non envoyée » à côté.
    expect(lignes.map((l) => l.querySelector('td.pv-name .pv-name__text')?.textContent?.trim())).toEqual(Object.values(LIBELLES));
    for (const ligne of lignes) {
      expect(ligne.querySelector('td[data-label="Envoyée"] .soft-toggle input[type="checkbox"]')).not.toBeNull();
      expect(ligne.querySelector('td[data-label="Code de paie"]')).not.toBeNull();
      expect(ligne.querySelector('td[data-label="Unité"]')).not.toBeNull();
    }
  });

  it('carte : contrôles interactifs, état désactivé et message d\'aide conservés', () => {
    const { fixture, racine, carte } = rendreCarte(config({ codesPaie: [{ code: 'RET01', intitule: 'Retard' }] }));
    const ligne = (nom: string) => [...racine.querySelectorAll('tbody tr')].find((l) => l.querySelector('td.pv-name')?.textContent?.includes(nom))!;

    const nuit = ligne('Nuit').querySelector<HTMLInputElement>('input[type="checkbox"]')!;
    nuit.click();
    fixture.detectChanges();
    expect(carte.modifie()).toBe(true);
    expect(carte.brouillon().find((v) => v.variable === 'Nuit')?.actif).toBe(false);

    const retard = ligne('Retard');
    expect(retard.querySelector<HTMLInputElement>('input[type="checkbox"]')!.disabled).toBe(true);
    expect(retard.querySelector('td[data-label="Envoyée"]')?.textContent).toContain('Choisissez d’abord le code de paie.');
    const liste = retard.querySelector<HTMLSelectElement>('td[data-label="Code de paie"] select')!;
    expect(liste.disabled).toBe(false);
    liste.value = 'RET01';
    liste.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    expect(retard.querySelector<HTMLInputElement>('input[type="checkbox"]')!.disabled).toBe(false);
  });

  it('carte : mois verrouillé, chaque carte de variable porte son message et ses contrôles désactivés', async () => {
    const { racine, fixture } = rendreCarte(config({ moisEnvoye: true, dernierEnvoi: '2026-11-01 → 2026-11-30', codesPaie: [{ code: 'RET01', intitule: 'Retard' }] }));
    // Adapté au lot C3 : la liste avec recherche (Absence) reçoit son état désactivé de ngModel, de façon asynchrone.
    await fixture.whenStable();
    fixture.detectChanges();

    const lignes = [...racine.querySelectorAll('tbody tr:not(.pv-codes-row)')];
    expect(lignes.length).toBe(9);
    for (const ligne of lignes) {
      expect(ligne.querySelector<HTMLInputElement>('input[type="checkbox"]')!.disabled).toBe(true);
      expect(ligne.querySelector('td[data-label="Envoyée"]')?.textContent).toContain('Mois déjà envoyé : modification impossible.');
      for (const champ of ligne.querySelectorAll<HTMLInputElement | HTMLSelectElement>('select, input[type="text"]')) expect(champ.disabled).toBe(true);
    }
  });

  it('destinations d\'une autre base : tableau réaffiché sous 768 px, cellules étiquetées, listes désactivées si verrouillé', () => {
    monterDestinations({ reponse: { moisEnvoye: true, dernierEnvoi: '2026-11-01 → 2026-11-30' } });
    const fixture = TestBed.createComponent(PayrollDestinations);
    fixture.componentInstance.ngOnInit();
    fixture.detectChanges();
    const racine = fixture.nativeElement as HTMLElement;

    expect(racine.querySelector('.soft-table-wrap')?.classList.contains('pd-wrap')).toBe(true);
    expect(reaffiche('pd-wrap')).toBe(true);
    const lignes = [...racine.querySelectorAll('tbody tr')];
    expect(lignes.map((l) => l.querySelector('td.pd-name')?.textContent?.trim())).toEqual(['Nuit', 'Retard']);
    for (const ligne of lignes) {
      expect(ligne.querySelector('td[data-label="Envoyée"]')).not.toBeNull();
      expect(ligne.querySelector<HTMLSelectElement>('td[data-label="Colonne"] select')!.disabled).toBe(true);
    }
  });
});

describe('Option « Remettre à 0 les salariés sans valeur à l\'envoi »', () => {
  afterEach(() => TestBed.resetTestingModule());

  const LIBELLE = 'Remettre à 0 les salariés sans valeur à l\'envoi';

  function rendre(reponse: PayrollVariablesConfig, options: { confirme?: boolean; permissions?: string[] } = {}) {
    const m = monter({ reponse, confirme: options.confirme, permissions: options.permissions });
    // Autre base : la section « Destinations » est rendue dans la carte et lit ses propres données.
    Object.assign(m.svc, { destinations: vi.fn(() => of({
      base: 'SAGE_DEMO', table: null, colonneMatricule: null, variables: [], tables: [], colonnes: [], moisEnvoye: false, dernierEnvoi: null,
    })) });
    const fixture = TestBed.createComponent(PayrollVariablesCard);
    fixture.componentInstance.ngOnInit();
    fixture.detectChanges();
    const racine = fixture.nativeElement as HTMLElement;
    const interrupteur = () => racine.querySelector<HTMLInputElement>('.pv-option input[type="checkbox"]');
    return { svc: m.svc, confirm: m.confirm, fixture, racine, carte: fixture.componentInstance, interrupteur };
  }

  it('cochée par défaut (aucune option enregistrée), avec son libellé, son texte d\'aide et le style des interrupteurs', () => {
    const { racine, interrupteur, carte } = rendre(config());

    expect(carte.remiseAZero()).toBe(true);
    expect(interrupteur()!.checked).toBe(true);
    expect(interrupteur()!.disabled).toBe(false);
    expect(racine.querySelector('.pv-option .soft-toggle .soft-toggle__track')).not.toBeNull();
    expect(racine.querySelector('.pv-option')?.textContent).toContain(LIBELLE);
    expect(racine.querySelector('.pv-option')?.textContent).toContain(
      'Évite qu\'un ancien cumul reste dans la paie quand un salarié n\'a plus d\'heures. À décocher si quelqu\'un saisit ces codes à la main dans la paie.');
    // Le libellé visible nomme la case (clic sur le texte).
    expect(interrupteur()!.closest('label')?.textContent).toContain(LIBELLE);
  });

  it('option enregistrée décochée : l\'interrupteur est décoché à l\'ouverture', () => {
    const { interrupteur, carte } = rendre(config({ remiseAZeroSansValeur: false, parDefaut: false }));

    expect(carte.remiseAZero()).toBe(false);
    expect(interrupteur()!.checked).toBe(false);
  });

  it('décocher crée un brouillon (bouton Enregistrer actif), Annuler le remet à la valeur lue', () => {
    const { fixture, interrupteur, carte } = rendre(config());
    expect(carte.modifie()).toBe(false);

    interrupteur()!.click();
    fixture.detectChanges();
    expect(carte.remiseAZero()).toBe(false);
    expect(carte.modifie()).toBe(true);

    carte.annuler();
    fixture.detectChanges();
    expect(carte.remiseAZero()).toBe(true);
    expect(carte.modifie()).toBe(false);
    expect(interrupteur()!.checked).toBe(true);
  });

  it('confirmation : « Remise à 0 des salariés sans valeur : désactivée », envoyée avec les variables en un seul appel', async () => {
    const { carte, svc, confirm } = rendre(config());
    carte.basculerRemiseAZero(false);

    await carte.enregistrer();

    expect(confirm.ask).toHaveBeenCalledTimes(1);
    expect(confirm.ask.mock.calls[0][0]).toBe(
      'Enregistrer pour la base SAGE_DEMO : Remise à 0 des salariés sans valeur : désactivée ? Le changement s\'appliquera au prochain envoi en paie.');
    expect(svc.save).toHaveBeenCalledTimes(1);
    const [variables, option] = svc.save.mock.calls[0] as unknown as [{ variable: string }[], boolean];
    expect(variables.length).toBe(9);
    expect(option).toBe(false);
  });

  it('activée de nouveau : la confirmation dit « activée » et cite aussi les variables modifiées', async () => {
    const { carte, confirm } = rendre(config({ remiseAZeroSansValeur: false, parDefaut: false }));
    carte.basculer('Nuit', false);
    carte.basculerRemiseAZero(true);

    await carte.enregistrer();

    expect(confirm.ask.mock.calls[0][0]).toContain('Nuit : non envoyée à la paie ; Remise à 0 des salariés sans valeur : activée ?');
  });

  it('annulée à la confirmation : rien n\'est envoyé', async () => {
    const { carte, svc } = rendre(config(), { confirme: false });
    carte.basculerRemiseAZero(false);

    await carte.enregistrer();

    expect(svc.save).not.toHaveBeenCalled();
    expect(carte.remiseAZero()).toBe(false);
  });

  it('sans changement de l\'option, elle est renvoyée telle quelle (la valeur lue)', async () => {
    const { carte, svc } = rendre(config({ remiseAZeroSansValeur: false, parDefaut: false }));
    carte.basculer('Nuit', false);

    await carte.enregistrer();

    expect((svc.save.mock.calls[0] as unknown[])[1]).toBe(false);
  });

  it('mois déjà envoyé : interrupteur désactivé avec son message, aucun bouton', () => {
    const { racine, interrupteur } = rendre(config({ moisEnvoye: true, dernierEnvoi: '2026-11-01 → 2026-11-30' }));

    expect(interrupteur()!.disabled).toBe(true);
    expect(racine.querySelector('.pv-option')?.textContent).toContain('Mois déjà envoyé : modification impossible.');
    expect(racine.querySelector('.pv-actions')).toBeNull();
  });

  it('sans permission : ni l\'interrupteur ni le tableau, seulement le message de permission', () => {
    const { racine, interrupteur } = rendre(config(), { permissions: ['SAGE:AUTRE:UPDATE'] });

    expect(interrupteur()).toBeNull();
    expect(racine.querySelector('.pv-option')).toBeNull();
    expect(racine.textContent).toContain('Vous n\'avez pas la permission de modifier la base SAGE_DEMO');
  });

  it('autre base : l\'option est proposée aussi', () => {
    const { interrupteur } = rendre(config({ typeBase: 'AUTRE' }));

    expect(interrupteur()).not.toBeNull();
  });

  it('largeur étroite : l\'option est hors du tableau (jamais masquée par la règle des tableaux sous 768 px)', () => {
    const { racine } = rendre(config());

    expect(racine.querySelector('.soft-table-wrap .pv-option')).toBeNull();
    expect(racine.querySelector('.pv-option')).not.toBeNull();
  });

  it('changementOption : texte seulement quand la valeur change', () => {
    expect(changementOption(true, true)).toBeNull();
    expect(changementOption(false, false)).toBeNull();
    expect(changementOption(true, false)).toBe('Remise à 0 des salariés sans valeur : désactivée');
    expect(changementOption(false, true)).toBe('Remise à 0 des salariés sans valeur : activée');
  });
});

describe('Confirmation de « Synchroniser vers SAGE » selon l\'option', () => {
  const REMISE = 'Les salariés de la sélection sans valeur sur la période seront remis à 0 pour les variables envoyées à la paie '
    + '(une variable non envoyée n\'est jamais modifiée).';

  it('option cochée : la remise à 0 est annoncée (texte inchangé), base standard et autre base', () => {
    expect(questionEnvoi(true, 'SAGE_DEMO', true)).toBe(`Pousser les HS vers SAGE (T_CUMSAL) ? ${REMISE}`);
    expect(questionEnvoi(false, 'AUTRE_BASE', true)).toBe(`Écrire les HS dans la base AUTRE_BASE ? ${REMISE}`);
  });

  it('option décochée : la remise à 0 n\'est pas annoncée', () => {
    expect(questionEnvoi(true, 'SAGE_DEMO', false)).toBe('Pousser les HS vers SAGE (T_CUMSAL) ?');
    expect(questionEnvoi(false, 'AUTRE_BASE', false)).toBe('Écrire les HS dans la base AUTRE_BASE ?');
  });
});

// =====================================================================================================
// Lot C3 : Absence envoyable (unité, heures par jour, codes d'absence comptés, confirmations)
// =====================================================================================================

const CODES_SAGE = [
  { code: 'ABS01', intitule: 'Absences' },
  { code: 'RET01', intitule: 'Retard' },
  ...Array.from({ length: 10 }, (_, i) => ({ code: `HS${String(i + 10)}`, intitule: `Heures ${i + 10}` })),
];

const CODES_ABSENCE: PayrollAbsenceCode[] = [
  { libelle: 'Abs non payés', compte: false, introuvable: false, nonPaye: true, suggere: true },
  { libelle: 'PERMISSION', compte: false, introuvable: false, nonPaye: false, suggere: false },
  { libelle: 'ABSENCE NON JUSTIFIEE', compte: true, introuvable: false, nonPaye: false, suggere: false },
];

/** Carte avec l'Absence cochée (code ABS01) ; unité et réglage au choix. */
function configAbsence(unite: 'H' | 'J', champs: Partial<PayrollVariablesConfig> = {}): PayrollVariablesConfig {
  return config({
    codesPaie: CODES_SAGE,
    variables: Object.keys(LIBELLES).map((n) => (n === 'Absence'
      ? variable(n, { actif: true, codePaie: 'ABS01', unite, cibleExistante: 'ABS01' })
      : variable(n))),
    heuresParJour: 8,
    absenceCodes: { parDefaut: true, sansCode: true, codes: CODES_ABSENCE },
    ...champs,
  });
}

function rendreAbsence(reponse: PayrollVariablesConfig, options: { confirme?: boolean } = {}) {
  const m = monter({ reponse, confirme: options.confirme });
  const fixture = TestBed.createComponent(PayrollVariablesCard);
  fixture.componentInstance.ngOnInit();
  fixture.detectChanges();
  const racine = fixture.nativeElement as HTMLElement;
  return { ...m, fixture, racine, carte: fixture.componentInstance };
}

type OptionsAbsence = { heuresParJour?: number | null; absenceCodes?: { sansCode: boolean; codes: string[] } | null };

function sauvegarde(svc: { save: { mock: { calls: unknown[][] } } }) {
  const [variables, remise, absence] = svc.save.mock.calls[0] as [{ variable: string; unite: string | null }[], boolean, OptionsAbsence];
  return { variables, remise, absence };
}

describe('Absence : liste des codes de paie avec recherche', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('les options affichent « code — intitulé » ; le code enregistré absent de SAGE reste proposé', () => {
    const { carte } = rendreAbsence(configAbsence('H'));
    const absence = carte.brouillon().find((v) => v.variable === 'Absence')!;

    expect(carte.optionsCodes(absence)[0]).toEqual({ value: 'ABS01', label: 'ABS01 — Absences' });
    expect(carte.optionsCodes({ ...absence, codePaie: 'OLD9' })[0]).toEqual({ value: 'OLD9', label: 'OLD9 — introuvable dans SAGE' });
    expect(carte.avecRecherche('Absence')).toBe(true);
    expect(carte.avecRecherche('Retard')).toBe(false);
  });

  it('taper dans le champ filtre la liste (code ou intitulé), « Aucun résultat » sinon', () => {
    const { racine, fixture } = rendreAbsence(configAbsence('H'));
    const champ = racine.querySelector<HTMLInputElement>('input[aria-label="Code de paie de Absence"]')!;
    expect(champ.getAttribute('role')).toBe('combobox');

    const saisir = (texte: string) => {
      champ.focus();
      champ.click();
      champ.value = texte;
      champ.dispatchEvent(new Event('input', { bubbles: true }));
      fixture.detectChanges();
      return Array.from(racine.querySelectorAll('[role="option"]')).map((o) => o.textContent!.trim());
    };

    expect(saisir('retard')).toEqual(['RET01 — Retard']);
    expect(saisir('HS1')).toHaveLength(10);
    expect(saisir('zzz')).toEqual([]);
    expect(racine.querySelector('.soft-combobox__empty')?.textContent).toContain('Aucun résultat');
  });
});

describe('Absence : unité et heures par jour', () => {
  afterEach(() => TestBed.resetTestingModule());

  const champHeures = (racine: HTMLElement) => racine.querySelector<HTMLInputElement>('input[aria-label="Heures par jour de l\'absence"]');
  const MESSAGE = (recu: string) =>
    `Le nombre d'heures par jour de l'absence doit être compris entre 1 et 24, avec une décimale au plus (valeur reçue : ${recu}).`;

  it('en heures : aucun champ « Heures par jour »', () => {
    expect(champHeures(rendreAbsence(configAbsence('H')).racine)).toBeNull();
  });

  it('en jours : champ à 8 par défaut avec son aide', () => {
    const { racine } = rendreAbsence(configAbsence('J'));

    expect(champHeures(racine)!.value).toBe('8');
    expect(racine.textContent).toContain('Heures par jour');
    expect(racine.textContent).toContain('L’absence est convertie en jours : heures ÷ heures par jour, arrondi au centième.');
  });

  it('passer l\'unité à « jours » affiche le champ ; la valeur lue (6,5) est reprise', () => {
    const { racine, fixture, carte } = rendreAbsence(configAbsence('H', { heuresParJour: 6.5 }));
    carte.changer('Absence', 'unite', 'J');
    fixture.detectChanges();

    expect(champHeures(racine)!.value).toBe('6.5');
  });

  it('saisie avec virgule acceptée ; champ vidé : erreur du serveur reprise à l\'identique, aucune confirmation', async () => {
    const { carte, confirm, svc } = rendreAbsence(configAbsence('J'));
    carte.changerHeuresParJour('6,5');
    expect(carte.heuresParJour()).toBe(6.5);
    expect(carte.modifie()).toBe(true);

    carte.changerHeuresParJour('');
    await carte.enregistrer();

    expect(carte.heuresParJour()).toBeNull();
    expect(carte.erreurs()).toEqual([MESSAGE('(vide)')]);
    expect(confirm.ask).not.toHaveBeenCalled();
    expect(svc.save).not.toHaveBeenCalled();
  });

  it.each([['0'], ['25'], ['7,25']])('valeur %s refusée avant l\'envoi, message du serveur', async (saisie) => {
    const { carte, svc } = rendreAbsence(configAbsence('J'));
    carte.changerHeuresParJour(saisie);

    await carte.enregistrer();

    expect(carte.erreurs()).toEqual([MESSAGE(saisie.replace(',', '.'))]);
    expect(svc.save).not.toHaveBeenCalled();
  });

  it('règle de validité : 1 à 24, une décimale', () => {
    expect([1, 7.5, 8, 24].every(heuresParJourValide)).toBe(true);
    expect([0, 0.5, 24.5, 7.25, null, undefined, Number.NaN].some((v) => heuresParJourValide(v))).toBe(false);
    expect(erreurHeuresParJour(8)).toBeNull();
    expect(texteHeures(7.5)).toBe('7,5');
    expect(texteHeures(null)).toBe('(vide)');
  });

  it('enregistrement : la confirmation cite « Absence : 6,5 heures par jour » ; le service reçoit les heures', async () => {
    const { carte, confirm, svc } = rendreAbsence(configAbsence('J'));
    carte.changerHeuresParJour('6,5');

    await carte.enregistrer();

    expect(confirm.ask.mock.calls[0][0]).toBe('Enregistrer pour la base SAGE_DEMO : Absence : 6,5 heures par jour ? Le changement s\'appliquera au prochain envoi en paie.');
    expect(sauvegarde(svc).absence).toEqual({ heuresParJour: 6.5, absenceCodes: null });
  });

  it('heures par jour inchangées : non renvoyées', async () => {
    const { carte, svc } = rendreAbsence(configAbsence('J'));
    carte.basculer('Nuit', false);

    await carte.enregistrer();

    expect(sauvegarde(svc).absence.heuresParJour).toBeNull();
  });

  it('changementHeuresParJour : texte seulement quand la valeur change', () => {
    expect(changementHeuresParJour(8, 8)).toBeNull();
    expect(changementHeuresParJour(8, 7.5)).toBe('Absence : 7,5 heures par jour');
  });

  it('mois déjà envoyé : champ « Heures par jour » désactivé', () => {
    const { racine } = rendreAbsence(configAbsence('J', { moisEnvoye: true, dernierEnvoi: '2026-11-01 → 2026-11-30' }));

    expect(champHeures(racine)!.disabled).toBe(true);
  });
});

describe('Absence : codes d\'absence comptés comme non justifiés', () => {
  afterEach(() => TestBed.resetTestingModule());

  const cases = (racine: HTMLElement) => Array.from(racine.querySelectorAll<HTMLLabelElement>('.pv-codes .pv-code'));
  const ligne = (racine: HTMLElement, libelle: string) => cases(racine).find((l) => l.textContent!.includes(libelle))!;

  it('liste : arbitrage sans code puis un code par ligne, cochés selon le réglage, avec les mentions', () => {
    const { racine } = rendreAbsence(configAbsence('H'));

    expect(racine.querySelector('.pv-codes__title')?.textContent).toBe('Codes d’absence comptés comme non justifiés');
    expect(cases(racine).map((l) => l.querySelector('.pv-code__label')!.textContent)).toEqual([
      'Arbitrage d\'absence sans code', 'Abs non payés', 'PERMISSION', 'ABSENCE NON JUSTIFIEE']);
    expect(ligne(racine, 'Arbitrage').querySelector('input')!.checked).toBe(true);
    expect(ligne(racine, 'ABSENCE NON JUSTIFIEE').querySelector('input')!.checked).toBe(true);
    expect(ligne(racine, 'PERMISSION').querySelector('input')!.checked).toBe(false);
    expect(ligne(racine, 'Abs non payés').textContent).toContain('non payé');
    expect(ligne(racine, 'Abs non payés').textContent).toContain('à envisager');
    expect(ligne(racine, 'PERMISSION').textContent).not.toContain('à envisager');
    expect(racine.querySelector('.pv-codes__hint')?.textContent).toContain('Le changement s’applique au prochain calcul');
  });

  it('code enregistré mais absent de T_CODEABSENCE : badge « code introuvable » et avertissement tant qu\'il est compté', () => {
    const introuvable: PayrollAbsenceCode = { libelle: 'ANCIEN CODE', compte: true, introuvable: true, nonPaye: false, suggere: false };
    const { racine, fixture, carte } = rendreAbsence(configAbsence('H', {
      absenceCodes: { parDefaut: false, sansCode: true, codes: [...CODES_ABSENCE, introuvable] },
    }));

    expect(ligne(racine, 'ANCIEN CODE').textContent).toContain('code introuvable');
    expect(racine.querySelector('.pv-codes__warn')?.textContent).toContain('Un code enregistré n\'existe plus dans les codes d\'absence : il reste compté tant qu\'il n\'est pas décoché.');
    expect(racine.querySelector('.pv-codes__warn')?.getAttribute('role')).toBe('status');

    carte.basculerCode('ANCIEN CODE', false);
    fixture.detectChanges();
    expect(racine.querySelector('.pv-codes__warn')).toBeNull();
    expect(ligne(racine, 'ANCIEN CODE').textContent).toContain('code introuvable');
  });

  it('cocher crée un brouillon (comparaison tolérante), Annuler revient au réglage lu', () => {
    const { racine, fixture, carte } = rendreAbsence(configAbsence('H'));
    expect(carte.modifie()).toBe(false);

    ligne(racine, 'PERMISSION').querySelector('input')!.click();
    fixture.detectChanges();
    expect(carte.estCompte('permission')).toBe(true);
    expect(carte.modifie()).toBe(true);

    carte.annuler();
    fixture.detectChanges();
    expect(carte.estCompte('PERMISSION')).toBe(false);
    expect(carte.modifie()).toBe(false);
  });

  it('mois déjà envoyé : les cases sont désactivées', () => {
    const { racine } = rendreAbsence(configAbsence('H', { moisEnvoye: true, dernierEnvoi: '2026-11-01 → 2026-11-30' }));

    expect(racine.querySelector<HTMLFieldSetElement>('fieldset.pv-codes')!.disabled).toBe(true);
  });

  it('codesAbsenceLus et changementsCodesAbsence : accents, casse et espaces ignorés ; arbitrage sans code', () => {
    const lus = codesAbsenceLus({ parDefaut: false, sansCode: true, codes: CODES_ABSENCE });
    expect(lus).toEqual({ sansCode: true, codes: ['ABSENCE NON JUSTIFIEE'] });
    expect(codesAbsenceLus(null)).toEqual({ sansCode: true, codes: [] });
    expect(changementsCodesAbsence(lus, { sansCode: true, codes: ['  absence non justifiée '] })).toEqual([]);
    expect(changementsCodesAbsence(lus, { sansCode: false, codes: ['PERMISSION'] })).toEqual([
      'Codes d\'absence comptés ajoutés : PERMISSION',
      'Codes d\'absence comptés retirés : ABSENCE NON JUSTIFIEE',
      'Arbitrage d\'absence sans code : non compté',
    ]);
  });

  it('confirmation d\'enregistrement : codes ajoutés, rappel « prochain calcul » ; le service reçoit les codes et l\'arbitrage sans code', async () => {
    const { racine, carte, confirm, svc } = rendreAbsence(configAbsence('H'));
    ligne(racine, 'PERMISSION').querySelector('input')!.click();
    carte.basculerSansCode(false);

    await carte.enregistrer();

    expect(confirm.ask).toHaveBeenCalledTimes(1);
    expect(confirm.ask.mock.calls[0][0]).toBe(
      'Enregistrer pour la base SAGE_DEMO : Codes d\'absence comptés ajoutés : PERMISSION ; Arbitrage d\'absence sans code : non compté ? '
      + 'Le changement s\'appliquera au prochain envoi en paie ; les codes d\'absence comptés, au prochain calcul '
      + '(recalculez les périodes déjà calculées avant de les envoyer).');
    expect(sauvegarde(svc).absence).toEqual({ heuresParJour: null, absenceCodes: { sansCode: false, codes: ['ABSENCE NON JUSTIFIEE', 'PERMISSION'] } });
  });

  it('annulée à la confirmation : rien n\'est envoyé', async () => {
    const { carte, svc } = rendreAbsence(configAbsence('H'), { confirme: false });
    carte.basculerCode('PERMISSION', true);

    await carte.enregistrer();

    expect(svc.save).not.toHaveBeenCalled();
  });

  it('réglage de codes inchangé : non renvoyé, pas de rappel « prochain calcul »', async () => {
    const { carte, svc, confirm } = rendreAbsence(configAbsence('H'));
    carte.basculer('Nuit', false);

    await carte.enregistrer();

    expect(sauvegarde(svc).absence.absenceCodes).toBeNull();
    expect(confirm.ask.mock.calls[0][0]).not.toContain('prochain calcul');
  });

  it('après enregistrement : brouillon remplacé par la réponse du serveur, carte non modifiée', async () => {
    const { carte, svc, toast } = rendreAbsence(configAbsence('H'));
    svc.save.mockReturnValueOnce(of(configAbsence('H', {
      absenceCodes: { parDefaut: false, sansCode: true, codes: CODES_ABSENCE.map((c) => ({ ...c, compte: true })) },
    })));
    carte.basculerCode('PERMISSION', true);

    await carte.enregistrer();

    expect(toast.success).toHaveBeenCalled();
    expect(carte.modifie()).toBe(false);
    expect(carte.estCompte('PERMISSION')).toBe(true);
  });
});

describe('Confirmation de « Synchroniser » : Absence envoyée en jours', () => {
  it('Absence en jours : l\'unité et les heures par jour sont précisées, avec ou sans remise à 0', () => {
    expect(questionEnvoi(true, 'SAGE_DEMO', false, 8)).toBe(
      'Pousser les HS vers SAGE (T_CUMSAL) ? L\'absence est envoyée en jours (heures ÷ 8 h par jour, arrondi au centième).');
    expect(questionEnvoi(false, 'AUTRE_BASE', false, 7.5)).toBe(
      'Écrire les HS dans la base AUTRE_BASE ? L\'absence est envoyée en jours (heures ÷ 7,5 h par jour, arrondi au centième).');
    expect(questionEnvoi(true, 'SAGE_DEMO', true, 6)).toContain(
      'remis à 0 pour les variables envoyées à la paie (une variable non envoyée n\'est jamais modifiée). L\'absence est envoyée en jours (heures ÷ 6 h par jour');
  });

  it('Absence en heures, décochée ou configuration illisible : rien n\'est ajouté', () => {
    expect(questionEnvoi(true, 'SAGE_DEMO', false, null)).toBe('Pousser les HS vers SAGE (T_CUMSAL) ?');
    expect(questionEnvoi(true, 'SAGE_DEMO', false)).toBe('Pousser les HS vers SAGE (T_CUMSAL) ?');
  });
});

// =====================================================================================================
// Lot C3 (correctifs) : libellés de codes longs et alignement vertical de la carte
// =====================================================================================================

describe('Carte : libellé de code complet et alignement vertical', () => {
  afterEach(() => TestBed.resetTestingModule());

  const LONG = { code: 'HS-FERIE', intitule: 'Heures travaillées jours fériés' };
  const LIBELLE_LONG = 'HS-FERIE — Heures travaillées jours fériés';

  function rendre(reponse: PayrollVariablesConfig) {
    monter({ reponse });
    const fixture = TestBed.createComponent(PayrollVariablesCard);
    fixture.componentInstance.ngOnInit();
    fixture.detectChanges();
    return { fixture, racine: fixture.nativeElement as HTMLElement, carte: fixture.componentInstance };
  }

  /** Styles du composant injectés par Angular (encapsulation émulée). */
  function css(): string {
    return [...document.head.querySelectorAll('style')].map((s) => s.textContent ?? '').join('\n').replace(/\s+/g, ' ');
  }

  it('liste avec recherche (Absence) : le libellé complet du code choisi est dans l\'infobulle', () => {
    const { racine, carte } = rendre(configAbsence('H', {
      codesPaie: [LONG, ...CODES_SAGE],
      variables: Object.keys(LIBELLES).map((n) => (n === 'Absence' ? variable(n, { actif: true, codePaie: 'HS-FERIE', unite: 'H', cibleExistante: 'HS-FERIE' }) : variable(n))),
    }));

    expect(racine.querySelector('soft-select[title]')?.getAttribute('title')).toBe(LIBELLE_LONG);
    expect(carte.titreCode(variable('Absence', { codePaie: 'hs-ferie' }))).toBe(LIBELLE_LONG);
  });

  it('liste native (Retard) : l\'infobulle porte le libellé complet ; code absent de SAGE : code seul ; aucun code : pas d\'infobulle', () => {
    const { racine, carte } = rendre(config({
      codesPaie: [LONG],
      variables: Object.keys(LIBELLES).map((n) => (n === 'Retard' ? variable(n, { codePaie: 'HS-FERIE', unite: 'H' }) : variable(n))),
    }));

    expect(racine.querySelector('select[aria-label="Code de paie de Retard"]')?.getAttribute('title')).toBe(LIBELLE_LONG);
    expect(carte.titreCode(variable('Retard', { codePaie: 'OLD9' }))).toBe('OLD9 — introuvable dans SAGE');
    expect(carte.titreCode(variable('Retard'))).toBeNull();
    expect(racine.querySelector('soft-select[title]')).toBeNull();
  });

  it('nom de la variable dans son propre élément, pastille « non envoyée » à part (même axe que les contrôles)', () => {
    const { racine } = rendre(config());
    const retard = [...racine.querySelectorAll('tbody tr')].find((l) => l.querySelector('td.pv-name')?.textContent?.includes('Retard'))!;

    expect(retard.querySelector('td.pv-name > .pv-name__text')?.textContent?.trim()).toBe('Retard');
    expect(retard.querySelector('td.pv-name > soft-badge')?.textContent).toContain('non envoyée à la paie');
    expect(racine.querySelectorAll('td.pv-name > .pv-name__text')).toHaveLength(9);
  });

  it('styles : première ligne de 44 px commune (nom, code lecture seule), cellules alignées en haut, champ de code plus large', () => {
    rendre(config());
    const styles = css();

    expect(styles).toMatch(/\.pv-name__text(\[[^\]]+\])? ?\{ ?display: block; line-height: 44px;/);
    expect(styles).toMatch(/\.pv-readonly(\[[^\]]+\])? ?\{[^}]*line-height: 44px;/);
    expect(styles).toMatch(/\.pv-table(\[[^\]]+\])? td(\[[^\]]+\])? ?\{ ?vertical-align: top;/);
    expect(styles).toMatch(/\.pv-field(\[[^\]]+\])? ?\{ ?min-width: 220px; max-width: 440px;/);
    // Sous 768 px (vue en cartes) le nom reprend sa mise en ligne normale, avec la pastille à sa suite.
    expect(styles).toMatch(/\.pv-name__text(\[[^\]]+\])? ?\{ ?display: inline; line-height: inherit;/);
  });

  it('destinations d\'une autre base : lignes centrées verticalement, libellé complet de la colonne en infobulle', () => {
    monterDestinations();
    const fixture = TestBed.createComponent(PayrollDestinations);
    fixture.componentInstance.ngOnInit();
    fixture.detectChanges();
    const racine = fixture.nativeElement as HTMLElement;

    expect(css()).toMatch(/\.pd-table(\[[^\]]+\])? td(\[[^\]]+\])? ?\{ ?vertical-align: middle;/);
    expect(racine.querySelector('select[aria-label="Colonne de Nuit"]')?.getAttribute('title')).toBe('HeuresNuit (decimal(18,2))');
    expect(racine.querySelector('select[aria-label="Colonne de Retard"]')?.getAttribute('title')).toBeNull();
    expect(fixture.componentInstance.titreColonne({ variable: 'Nuit', libelle: 'Nuit', actif: true, colonne: 'Disparue', decimalesRequises: 0 })).toBe('Disparue (introuvable dans la table)');
  });
});
