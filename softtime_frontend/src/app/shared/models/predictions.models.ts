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
  modeles: MlModelInfo[];
}

export interface MlAbsenceDay {
  date: string;
  probabilite: number;
  niveau: number;
  planningConnu: boolean;
}

export interface MlAbsenceForecast {
  matricule: string;
  nom: string | null;
  prenom: string | null;
  departement: string | null;
  /** Risque d'au moins une absence sur l'horizon (0..1). */
  risque: number;
  niveau: number;
  jours: MlAbsenceDay[];
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
  absencePredictions: number;
  hsPredictions: number;
}
