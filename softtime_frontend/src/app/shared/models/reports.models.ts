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

export interface ReportFilter {
  matriculeFrom?: string | null;
  matriculeTo?: string | null;
  from: string;
  to: string;
  branche?: string | null;
}

/** Generic report rows are returned as untyped records by the backend. */
export type ReportRow = Record<string, unknown>;
