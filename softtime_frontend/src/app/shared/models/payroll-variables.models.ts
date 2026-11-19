import { SageConstantOption } from './catalog.models';

/** Une des neuf variables de « Variables envoyées à la paie » (api/v1/payroll-variables). */
export interface PayrollVariable {
  /** Exo130, Exo150, I130, I150, Ferie, Dimanche, Nuit, Retard, Absence. */
  variable: string;
  libelle: string;
  actif: boolean;
  /** Base SAGE : code de paie propre à la base (Retard, Absence). */
  codePaie: string | null;
  /** Retard : « H » (heures) ; Absence : « H » ou « J » (jours). */
  unite: string | null;
  /** Ancienne colonne saisie (lot B), plus utilisée : Retard et Absence utilisent leur destination (cibleExistante). */
  colonneCible: string | null;
  /** Lecture seule : code constante commun (SAGE, heures sup.) ou destination T_PAIE_ECRITURE (autre base). */
  cibleExistante: string | null;
  /** L'envoi réel de la variable n'existe pas encore (aucune depuis le lot C3). */
  sansEnvoi: boolean;
}

export interface PayrollVariablesConfig {
  base: string;
  typeBase: 'STANDARD' | 'AUTRE' | string;
  parDefaut: boolean;
  moisEnvoye: boolean;
  dernierEnvoi: string | null;
  variables: PayrollVariable[];
  colonnesCibles: string[];
  /** Base SAGE : codes de paie lus dans SAGE (T_CST), pour la liste du code de Retard. */
  codesPaie?: SageConstantOption[];
  /** Base SAGE : message si les codes n'ont pas pu être lus. */
  avertissementCodes?: string | null;
  /** Option de la base : l'envoi remet à 0 les salariés du périmètre sans valeur. Absent : vrai (défaut). */
  remiseAZeroSansValeur?: boolean;
  /** Absence en jours : nombre d'heures d'une journée. Absent : 8. */
  heuresParJour?: number;
  /** Codes d'absence comptés comme non justifiés dans l'Absence (lot C3). */
  absenceCodes?: PayrollAbsenceCodes | null;
  /** Domaines envoyés à la paie par la base, exigés à la clôture (lot D5). Absent : désactivés. */
  domaines?: PayrollDomains | null;
}

/** Activation de la cantine et des frais de déplacement pour la base active (désactivés par défaut). */
export interface PayrollDomains {
  cantine: boolean;
  frais: boolean;
  /** Code de la constante SAGE de la cantine : en lecture, le code utilisé ; à l'enregistrement, absent = inchangé. */
  codeCantine?: string | null;
  /** En lecture : aucun code configuré, le code historique CANTINE_CODE est utilisé. */
  codeCantineParDefaut?: boolean;
}

/** Réglage « Codes d'absence comptés comme non justifiés » de la base. */
export interface PayrollAbsenceCodes {
  /** Aucun réglage enregistré : « ABSENCE NON JUSTIFIEE » et l'arbitrage sans code. */
  parDefaut: boolean;
  /** Un arbitrage sans code est compté. */
  sansCode: boolean;
  codes: PayrollAbsenceCode[];
}

/** Un code d'absence proposé dans la carte. */
export interface PayrollAbsenceCode {
  libelle: string;
  /** Compté comme non justifié. */
  compte: boolean;
  /** Enregistré dans le réglage mais absent de T_CODEABSENCE. */
  introuvable: boolean;
  /** NOT_PAY = 1 dans T_CODEABSENCE. */
  nonPaye: boolean;
  /** Le libellé évoque « non justifiée » ou « non payé » (simple suggestion). */
  suggere: boolean;
}

/** Réglage des codes d'absence comptés tel qu'il est enregistré. */
export interface PayrollAbsenceCodesSave {
  sansCode: boolean;
  codes: string[];
}

/** Champs facultatifs de l'enregistrement de la carte (absents : inchangés). */
export interface PayrollVariablesSaveOptions {
  remiseAZeroSansValeur?: boolean;
  heuresParJour?: number | null;
  absenceCodes?: PayrollAbsenceCodesSave | null;
}

export interface PayrollVariableSave {
  variable: string;
  actif: boolean;
  codePaie: string | null;
  unite: string | null;
  colonneCible: string | null;
}

export interface PayrollVariablesActive {
  base: string;
  /** Variables réellement envoyées à la paie (cochées et envoyables). */
  actives: string[];
  /** Option de la base : l'envoi remet à 0 les salariés sans valeur (annoncé dans la confirmation de l'envoi). Absent : vrai. */
  remiseAZeroSansValeur?: boolean;
  /** Unité d'envoi de l'Absence (« H » ou « J »). Absent : heures. */
  uniteAbsence?: string;
  /** Absence en jours : nombre d'heures d'une journée. Absent : 8. */
  heuresParJour?: number;
}

/** Colonne d'une table de la base de paie d'une autre base. */
export interface PayrollTargetColumn {
  nom: string;
  /** Type SQL affiché, par ex. « decimal(18,2) ». */
  type: string;
  numerique: boolean;
  /** Peut recevoir une valeur à deux décimales. */
  accepteDecimales: boolean;
}

/** Destination d'une variable envoyable dans une autre base. */
export interface PayrollWriteTarget {
  variable: string;
  libelle: string;
  actif: boolean;
  colonne: string | null;
  /** Décimales que la colonne doit accepter (2 pour Retard et Absence, 0 sinon). */
  decimalesRequises: number;
}

/** Destinations d'une autre base (T_PAIE_ECRITURE). */
export interface PayrollWriteTargets {
  base: string;
  table: string | null;
  colonneMatricule: string | null;
  variables: PayrollWriteTarget[];
  tables: string[];
  colonnes: PayrollTargetColumn[];
  moisEnvoye: boolean;
  dernierEnvoi: string | null;
}

export interface PayrollWriteTargetsSave {
  table: string | null;
  colonneMatricule: string | null;
  variables: { variable: string; colonne: string | null }[];
}
