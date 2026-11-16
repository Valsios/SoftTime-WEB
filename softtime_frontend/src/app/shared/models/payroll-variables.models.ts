/** Une des neuf variables de « Variables envoyées à la paie » (api/v1/payroll-variables). */
export interface PayrollVariable {
  /** Exo130, Exo150, I130, I150, Ferie, Dimanche, Nuit, Retard, Absence. */
  variable: string;
  libelle: string;
  actif: boolean;
  /** Base SAGE : code de paie (Retard, Absence). */
  codePaie: string | null;
  /** Retard, Absence : « H » (heures) ou « J » (jours). */
  unite: string | null;
  /** Autre base : colonne cible (Retard, Absence). */
  colonneCible: string | null;
  /** Heures supplémentaires : code constante (SAGE) ou colonne T_PAIE_ECRITURE (autre base), lecture seule. */
  cibleExistante: string | null;
  /** Retard, Absence : l'envoi réel n'existe pas encore (lot C). */
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
  actives: string[];
}
