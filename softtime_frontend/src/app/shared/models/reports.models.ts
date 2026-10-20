export interface Canteen {
  id: number;
  matricule?: string | null;
  dateDeb?: string | null;
  dateFin?: string | null;
  total?: number | null;
}

export interface Travel {
  id: number;
  matricule?: string | null;
  dateDeb?: string | null;
  dateFin?: string | null;
  total?: number | null;
}

/** Anomalie du calcul cantine : pointage contredisant une absence complète. */
export interface CanteenAnomaly {
  matricule?: string | null;
  date?: string | null;
  motif?: string | null;
}

/** Résultat d'une synchronisation de cumuls (cantine / frais) vers SAGE. */
export interface ExtrasSyncResult {
  database: string;
  typeBase: string;
  employeesSynced: number;
  cumulsUpdated: number;
  message: string;
}

/** Résultat d'une purge de lignes cantine / frais. */
export interface PurgeExtrasResult {
  lignesSupprimees: number;
  message: string;
}

export interface AuditEvent {
  id: number;
  table?: string | null;
  column?: string | null;
  user?: string | null;
  action?: string | null;
  date?: string | null;
  newValue?: string | null;
  oldValue?: string | null;
  base?: string | null;
}

export interface AuditConfig {
  id: number;
  table?: string | null;
  column?: string | null;
  type?: string | null;
  enabled?: boolean | null;
  base?: string | null;
}

export interface AuditTypeOption {
  value: string;
  label: string;
}

// --- Archives des suppressions (ex-tables « Historique* ») ---

export interface HistoryPointage {
  id: number;
  matricule?: string | null;
  date?: string | null;
  heure?: string | null;
  type?: string | null;
  supprimeLe?: string | null;
}

export interface HistoryAnomalie {
  id: number;
  matricule?: string | null;
  dateIn?: string | null;
  dateOut?: string | null;
  hEntree?: string | null;
  hSortie?: string | null;
  absAm?: boolean | null;
  absPm?: boolean | null;
  intituleAbsence?: string | null;
  supprimeLe?: string | null;
}

export interface HistoryHs {
  id: number;
  matricule?: string | null;
  periodeDebut?: string | null;
  periodeFin?: string | null;
  exo130?: number | null;
  exo150?: number | null;
  i130?: number | null;
  i150?: number | null;
  supprimeLe?: string | null;
}

export interface HistoryDeleteResult {
  supprimees: number;
  message: string;
}

export interface ReportFilter {
  matriculeFrom?: string | null;
  matriculeTo?: string | null;
  from: string;
  to: string;
  branche?: string | null;
}

/** Generic report rows are returned as untyped records by the backend. */
export type ReportRow = Record<string, unknown>;
