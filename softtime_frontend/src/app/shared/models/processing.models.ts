export interface Shift {
  id: number;
  noShift?: number | null;
  ha?: string | null;
  pause?: string | null;
  hd?: string | null;
  intitule?: string | null;
}

export interface EmployeePlanning {
  id: number;
  cardPaieId: number;
  noShift?: number | null;
  dateP?: string | null;
  ha?: string | null;
  pause?: string | null;
  hd?: string | null;
  off?: boolean | null;
  matricule?: string | null;
  nom?: string | null;
  prenom?: string | null;
}

export interface PlanningEmployee {
  cardPaieId: number;
  matricule?: string | null;
  nom?: string | null;
  prenom?: string | null;
}

/** Salarié + nombre de jours planifiés sur la période (grille paginée du planning). */
export interface PlanningEmployeeStat extends PlanningEmployee {
  plannedDays: number;
}

export interface Punch {
  id: number;
  matricule?: string | null;
  date?: string | null;
  heure?: string | null;
  type?: string | null;
  importDate?: string | null;
}

export interface ImportPunchesRequest {
  matriculeFrom?: string | null;
  matriculeTo?: string | null;
  from: string;
  to: string;
}

/** Correction d'un pointage importé : les champs non renseignés restent inchangés. */
export interface PunchUpdate {
  date?: string | null;
  heure?: string | null;
  type?: string | null;
}

/** Résultat de la purge d'une période de traitement. */
export interface PurgePunchesResult {
  pointages: number;
  corrections: number;
  anomalies: number;
  correctionsManuellesConservees: number;
  message: string;
}

/** Résultat de la purge des validations d'une semaine. */
export interface PurgeValidationResult {
  validations: number;
  corrections: number;
  message: string;
}

export interface ImportResult {
  imported: number;
  skipped: number;
  message: string;
}

/** Résultat du pré-traitement des pointages (pointage → heures corrigées + anomalies). */
export interface PreprocessResult {
  correctionsCreees: number;
  anomaliesCreees: number;
  joursIgnores: number;
  message: string;
}

export interface Anomaly {
  id: number;
  matricule?: string | null;
  dateIn?: string | null;
  dateOut?: string | null;
  h1?: string | null;
  h2?: string | null;
  hEntree?: string | null;
  hPs?: string | null;
  hPe?: string | null;
  hSortie?: string | null;
  absAm?: boolean | null;
  absPm?: boolean | null;
  feries?: boolean | null;
  feriesAm?: boolean | null;
  feriesPm?: boolean | null;
  intituleAbsence?: string | null;
}

/** Arbitrage d'une anomalie : les champs absents (undefined) restent inchangés côté serveur. */
export interface AnomalyUpdate {
  dateIn?: string | null;
  dateOut?: string | null;
  hEntree?: string | null;
  hPs?: string | null;
  hPe?: string | null;
  hSortie?: string | null;
  absAm?: boolean | null;
  absPm?: boolean | null;
  feries?: boolean | null;
  feriesAm?: boolean | null;
  feriesPm?: boolean | null;
  intituleAbsence?: string | null;
}

export interface AnomalyValidationResult {
  correctionsCreees: number;
  anomaliesSupprimees: number;
  message: string;
}

export interface CorrectedHour {
  id: number;
  matricule?: string | null;
  dateIn?: string | null;
  dateOut?: string | null;
  hEntree?: string | null;
  hPs?: string | null;
  hPe?: string | null;
  hSortie?: string | null;
  absAm?: boolean | null;
  absPm?: boolean | null;
  intituleAbsence?: string | null;
  retard?: string | null;
  ferie?: boolean | null;
  ferieAm?: boolean | null;
  feriePm?: boolean | null;
  validerCorrection?: boolean | null;
  validerHs?: boolean | null;
  hs?: number | null;
  mNuit?: number | null;
  mDimanche?: number | null;
  mFeries?: number | null;
}

export interface PeriodRequest {
  matriculeFrom?: string | null;
  matriculeTo?: string | null;
  from: string;
  to: string;
  branche?: string | null;
  departement?: string | null;
  /** Numéro de page (1-based), uniquement pour les listes paginées. */
  page?: number;
  /** Taille de page, plafonnée à 100 par le serveur. */
  pageSize?: number;
}

/** Période + pagination (listes massives : pointages, heures corrigées). */
export interface PagedPeriodRequest extends PeriodRequest {
  page?: number;
  pageSize?: number;
}

export interface WeeklyValidationRequest extends PeriodRequest {
  selectedMatricules: string[];
}

export interface WeeklyHs {
  id: number;
  matricule?: string | null;
  dateDeb?: string | null;
  dateFin?: string | null;
  ht?: number | null;
  hs?: number | null;
  nuit?: number | null;
  dimanche?: number | null;
  feries?: number | null;
  retard?: number | null;
  absence?: number | null;
  validated?: boolean | null;
}

export interface HsExo {
  oid: number;
  matricule?: string | null;
  numSal?: number | null;
  exo130?: number | null;
  exo150?: number | null;
  i130?: number | null;
  i150?: number | null;
  ferie?: number | null;
  nuit?: number | null;
  dim?: number | null;
  retard?: number | null;
  absence?: number | null;
}

/** Jour détaillé du calcul des heures supplémentaires d'un salarié. */
export interface OvertimeDetailDay {
  date?: string | null;
  entree?: string | null;
  sortie?: string | null;
  /** Heures retenues (après arrondis et pause manquante) : base du calcul hebdomadaire. */
  heuresTravaillees: number;
  heuresSupJournalieres: number;
  heuresNuit: number;
  dimanche: boolean;
  ferie: boolean;
  retard?: string | null;
  absenceAm: boolean;
  absencePm: boolean;
  conge: boolean;
  jourTravaille: boolean;
  /** Heures réellement pointées, avant arrondi et déduction de la pause. */
  heuresReelles: number;
}

/** Semaine du calcul EXO/IMPO d'un salarié. */
export interface OvertimeDetailWeek {
  semaine?: number | null;
  debut?: string | null;
  fin?: string | null;
  hs: number;
  hs1: number;
  hs2: number;
}

/** Détail du calcul HS d'un salarié (remplace le `DetailHS` du desktop). */
export interface OvertimeDetail {
  matricule?: string | null;
  from: string;
  to: string;
  jours: OvertimeDetailDay[];
  semaines: OvertimeDetailWeek[];
  exoImpo?: HsExo | null;
}
