/** Modèle entraîné (métadonnées exposées par le backend, sans binaire). */
export interface MlModelInfo {
  id: number;
  typeModele: string;
  version: string;
  algorithme: string;
  statut: string;
  estActif: boolean;
  entraineLe: string;
  periodeDebut: string;
  periodeFin: string;
  nbLignes: number;
  nbPositifs: number | null;
  /** Métriques sérialisées (PascalCase) : Auc, PrAuc, Brier, Mae, Rmse, R2, BaselineAuc, BaselineMae. */
  metriquesJson: string;
  commentaire: string | null;
}

export interface MlStatus {
  enabled: boolean;
  /** Pret | Baseline | Desactive */
  etat: string;
  message: string | null;
  derniereGeneration: string | null;
  /** Plafond hebdomadaire des HS exonérées (au-delà, imposables), en heures. */
  plafondHsExonere: number;
  modeles: MlModelInfo[];
}

export interface MlHsForecast {
  matricule: string;
  nom: string | null;
  prenom: string | null;
  departement: string | null;
  semaineDebut: string;
  heuresPrevues: number;
  borneBasse: number;
  borneHaute: number;
  probabiliteDepassement: number;
  niveau: number;
}

export interface MlJobState {
  running: boolean;
  message: string;
  startedAt: string | null;
  finishedAt: string | null;
  lastError: string | null;
}

export interface MlPipelineResult {
  etat: string;
  message: string;
  dailyRows: number;
  weeklyRows: number;
  hsPredictions: number;
}

/** Totaux HS prévus d'une semaine de l'horizon (0 salarié : planning pas encore saisi). */
export interface MlHsWeek {
  semaineDebut: string;
  salaries: number;
  /** Salariés dont les HS prévues dépassent le plafond exonéré. */
  auDelaPlafond: number;
  heuresPrevues: number;
  borneBasse: number;
  borneHaute: number;
}

/** Prévu / réel d'une semaine terminée (salariés présents des deux côtés). */
export interface MlHsAccuracy {
  semaineDebut: string;
  salaries: number;
  heuresPrevues: number;
  heuresReelles: number;
  /** Écart absolu moyen par salarié, en heures. */
  ecartMoyen: number;
  /** Part des salariés dont le réel tombe dans la fourchette (0..1). */
  couverture: number;
}
