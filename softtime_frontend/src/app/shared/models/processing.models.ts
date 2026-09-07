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

export interface ImportResult {
  imported: number;
  skipped: number;
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
  hSortie?: string | null;
  absAm?: boolean | null;
  absPm?: boolean | null;
  feriesAm?: boolean | null;
  feriesPm?: boolean | null;
  intituleAbsence?: string | null;
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
