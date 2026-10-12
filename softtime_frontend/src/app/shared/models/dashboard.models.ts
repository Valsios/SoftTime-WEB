export interface DashboardStats {
  totalEmployes: number;
  kpi: DashboardKpi;
  absences: DashboardAbsences;
  retards: DashboardRetards;
}

export interface DashboardKpi {
  cartesActives: number;
  anomalies: number;
  correctionsManuelles: number;
  semainesValidees: number;
  semainesEnAttente: number;
  heuresTravaillees: number;
  heuresSupplementaires: number;
}

export interface DashboardGroup {
  periode: string;
  count: number;
  totalMinutes: number;
}

export interface DashboardTop {
  matricule: string;
  nom?: string | null;
  prenom?: string | null;
  departement?: string | null;
  count: number;
  totalMinutes: number;
}

export interface DashboardAbsences {
  total: number;
  explicites: number;
  /** Jours planifiés sans heure corrigée — les congés SAGE ne sont pas encore exclus. */
  planifiesSansPointage: number;
  stats: DashboardGroup[];
  top: DashboardTop[];
}

export interface DashboardRetards {
  total: number;
  totalMinutes: number;
  stats: DashboardGroup[];
  top: DashboardTop[];
}
