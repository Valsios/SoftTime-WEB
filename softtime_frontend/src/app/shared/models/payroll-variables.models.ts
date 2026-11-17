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
  /** Autre base : colonne saisie pour Absence (non envoyée). Retard utilise sa destination (cibleExistante). */
  colonneCible: string | null;
  /** Lecture seule : code constante commun (SAGE, heures sup.) ou destination T_PAIE_ECRITURE (autre base). */
  cibleExistante: string | null;
  /** Absence : l'envoi réel n'existe pas encore (prochaine étape). */
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
  /** Décimales que la colonne doit accepter (2 pour Retard, 0 sinon). */
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
