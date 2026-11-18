/** Envoi en paie d'une période, avec son instantané (api/v1/payroll-snapshots, api/v1/payroll-closures/periods). */
export interface PayrollSendSummary {
  envoiId: number;
  bddSage: string;
  periodeDebut: string;
  periodeFin: string;
  typeBase: string;
  envoyeLe: string;
  envoyePar?: string | null;
  statut: string;
  lignesEnvoi: number;
  instantaneId?: number | null;
  avecFiltre?: boolean | null;
  departement?: string | null;
  matriculeDe?: string | null;
  matriculeA?: string | null;
  versionFormat?: number | null;
  empreinte?: string | null;
  nbLignes?: number | null;
  nbSansLigne?: number | null;
  nbNonRemis?: number | null;
}

/** Clôture enregistrée (définitive). */
export interface PayrollClosure {
  id: number;
  bddSage: string;
  periodeDebut: string;
  periodeFin: string;
  clotureLe: string;
  cloturePar: string;
  envoiId: number;
  instantaneId: number;
  empreinte: string;
  statutControle: string;
  resumeControle: string;
}

/** État d'une période envoyée : « ENVOYEE » ou « CLOTUREE ». */
export interface PayrollPeriodState {
  periodeDebut: string;
  periodeFin: string;
  etat: 'ENVOYEE' | 'CLOTUREE';
  dernierEnvoi: PayrollSendSummary;
  cloture?: PayrollClosure | null;
}

/** Périodes envoyées de la base active et droit de clôturer de l'utilisateur. */
export interface PayrollPeriods {
  bddSage: string;
  peutCloturer: boolean;
  periodes: PayrollPeriodState[];
}

/** Période clôturée (bornes incluses). */
export interface ClosedPeriod {
  debut: string;
  fin: string;
}

/** Périodes clôturées qui recoupent une sélection (bandeau des pages de modification). */
export interface PayrollClosureState {
  cloturee: boolean;
  periodes: ClosedPeriod[];
  message?: string | null;
}
