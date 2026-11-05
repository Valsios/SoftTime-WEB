/** Clôture de paie assistée : types alignés sur la réponse de GET api/closing/analysis (lecture seule). */

export type ClosingAnomalyType =
  | 'PointageIncomplet'
  | 'PointageManquant'
  | 'PlanningSansPointage'
  | 'PointageSansPlanning'
  | 'DoublonExact'
  | 'QuasiDoublon'
  | 'FerieNonApplique';

export type ClosingSeverity = 'Information' | 'Mineure' | 'Importante' | 'Critique';

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
  /** Férié non appliqué seulement : un pointage existe ce jour-là pour le salarié (null pour les autres types). */
  pointageExiste?: boolean | null;
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
  /** Salariés sans aucun planning sur la période, non contrôlés par « pointage sans planning ». */
  salariesSansPlanningExclus?: number;
}

/** Une étape de la préparation de paie : Fait, AFaire, Information ou NonDeterminable (jamais deviné). */
export interface ClosingChecklistLine {
  code: string;
  libelle: string;
  etat: 'Fait' | 'AFaire' | 'Information' | 'NonDeterminable' | string;
  detail: string;
  nombre?: number | null;
}

/** Réponse de GET api/closing/checklist : séparée de l'analyse, sans effet sur le score ni le statut. */
export interface ClosingChecklist {
  from: string;
  to: string;
  departement: string | null;
  lignes: ClosingChecklistLine[];
}

export interface ClosingQuery {
  /** Jour de début, AAAA-MM-JJ (envoyé en UTC avec Z). */
  from?: string | null;
  to?: string | null;
  departement?: string | null;
  includeComparison?: boolean;
  /** Ajoute à la liste les anomalies « Information » (les comptages les incluent toujours). */
  includeInformation?: boolean;
  page?: number;
  pageSize?: number;
  /** Changement de page seulement : le serveur peut réutiliser, 60 s au plus, l'analyse déjà calculée. */
  reuseCache?: boolean;
  /** Restreint la LISTE à ces niveaux (Information, Mineure, Importante, Critique) ; comptages, score et statut ne changent pas. */
  gravites?: readonly string[];
  /** « gravite » : liste du plus grave au moins grave, puis ordre habituel. */
  tri?: 'gravite';
}
