export interface DashboardStats {
  totalEmployes: number;
  kpi: DashboardKpi;
  absences: DashboardAbsences;
  retards: DashboardRetards;
  series: DashboardSerie[];
  repartition: DashboardRepartition;
  assiduite: DashboardAssiduite;
}

/**
 * Les taux sont calculés côté serveur :
 * - `tauxAbsenteisme` = jours d'absence / jours planifiés ;
 * - `tauxPonctualite` = jours travaillés sans retard / jours travaillés.
 */
export interface DashboardKpi {
  cartesActives: number;
  anomalies: number;
  correctionsManuelles: number;
  semainesValidees: number;
  semainesEnAttente: number;
  heuresTravaillees: number;
  heuresSupplementaires: number;
  joursPlanifies: number;
  joursTravailles: number;
  joursAbsence: number;
  joursRetard: number;
  minutesRetard: number;
  tauxAbsenteisme: number;
  tauxPonctualite: number;
}

/** Un point de courbe : une ligne par période de la plage (0 si rien à signaler). */
export interface DashboardSerie {
  periode: string;
  heures: number;
  heuresSupplementaires: number;
}

/** Ligne de répartition d'un axe (catégorie, département). */
export interface DashboardSlice {
  libelle: string;
  effectif: number;
  absences: number;
  retards: number;
  minutesRetard: number;
  heuresSupplementaires: number;
}

export interface DashboardRepartition {
  parCategorie: DashboardSlice[];
  parDepartement: DashboardSlice[];
}

/** Ligne de classement d'assiduité (un salarié sur la période). */
export interface DashboardAssiduiteRow {
  matricule: string;
  nom?: string | null;
  prenom?: string | null;
  departement?: string | null;
  categorie?: string | null;
  joursTravailles: number;
  joursAbsence: number;
  joursRetard: number;
  minutesRetard: number;
  tauxPonctualite: number;
}

export interface DashboardAssiduite {
  plusPonctuels: DashboardAssiduiteRow[];
  moinsPonctuels: DashboardAssiduiteRow[];
}

/** Valeurs proposées par les filtres de la page. */
export interface DashboardFilters {
  departements: string[];
  categories: { id: number; libelle: string }[];
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
