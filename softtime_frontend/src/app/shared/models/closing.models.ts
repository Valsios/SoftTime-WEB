/** Clôture de paie assistée : types alignés sur la réponse de GET api/closing/analysis (lecture seule). */

export type ClosingAnomalyType =
  | 'PointageIncomplet'
  | 'PointageManquant'
  | 'PlanningSansPointage'
  | 'PointageSansPlanning'
  | 'DoublonExact'
  | 'QuasiDoublon'
  | 'FerieNonApplique';

export type ClosingSeverity = 'Mineure' | 'Importante' | 'Critique';

export type ClosingStatus = 'Pret' | 'ACorriger' | 'Bloquant';

export interface ClosingCount {
  cle: string;
  nombre: number;
}

export interface ClosingAnomalyView {
  type: ClosingAnomalyType | string;
  gravite: ClosingSeverity | string;
  poids: number;
  matricule: string;
  nom: string | null;
  prenom: string | null;
  departement: string | null;
  date: string;
  detail: string;
  heures: number | null;
}

export interface ClosingPage<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

export interface ClosingDepartment {
  departement: string | null;
  score: number | null;
  joursEmployeControles: number;
  nombreAnomalies: number;
  parGravite: ClosingCount[];
  parType: ClosingCount[];
}

export interface ClosingTypeDelta {
  type: ClosingAnomalyType | string;
  precedent: number;
  actuel: number;
  ecart: number;
}

export interface ClosingComparison {
  from: string;
  to: string;
  scorePrecedent: number | null;
  joursEmployeControlesPrecedent: number;
  ecartScore: number | null;
  parType: ClosingTypeDelta[];
}

export interface ClosingReport {
  from: string;
  to: string;
  departement: string | null;
  statut: ClosingStatus | null;
  statutLibelle: string | null;
  seuilBloquant: number;
  score: number | null;
  formule: string;
  joursEmployeControles: number;
  nombreAnomalies: number;
  parGravite: ClosingCount[];
  parType: ClosingCount[];
  anomalies: ClosingPage<ClosingAnomalyView>;
  parDepartement: ClosingDepartment[];
  comparaison: ClosingComparison | null;
  nomsAffiches: boolean;
}

export interface ClosingQuery {
  /** Jour de début, AAAA-MM-JJ (envoyé en UTC avec Z). */
  from?: string | null;
  to?: string | null;
  departement?: string | null;
  includeComparison?: boolean;
  page?: number;
  pageSize?: number;
}
