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
  /** Domaine de l'envoi (migration 011) : heures, cantine ou frais de déplacement. */
  domaine?: PayrollDomain;
}

/** Domaine d'un envoi en paie. */
export type PayrollDomain = 'HEURES' | 'CANTINE' | 'FRAIS';

/** Envoi, instantané et empreinte d'un domaine figés par une clôture. */
export interface PayrollClosureDomain {
  domaine: PayrollDomain;
  envoiId: number;
  instantaneId: number;
  versionFormat: number;
  empreinte: string;
}

/**
 * État d'un domaine pour une période : figé (clôture faite), envoyé (dernier envoi réussi, sans filtre, avec instantané,
 * sur les bornes exactes), manquant (exigé mais pas encore envoyé ainsi : motif du serveur) ou non exigé.
 */
export interface PayrollDomainState {
  domaine: PayrollDomain;
  libelle: string;
  actif: boolean;
  exige: boolean;
  etat: 'FIGE' | 'ENVOYE' | 'MANQUANT' | 'NON_EXIGE';
  dernierEnvoi?: PayrollSendSummary | null;
  empreinte?: string | null;
  motif?: string | null;
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
  /** Cantine et frais figés par la clôture (vide si aucun n'était exigé). */
  domaines?: PayrollClosureDomain[];
}

/** État d'une période envoyée : « ENVOYEE » ou « CLOTUREE ». */
export interface PayrollPeriodState {
  periodeDebut: string;
  periodeFin: string;
  etat: 'ENVOYEE' | 'CLOTUREE';
  dernierEnvoi: PayrollSendSummary;
  cloture?: PayrollClosure | null;
  /** État de chaque domaine : heures, cantine, frais (absent avec un serveur antérieur au lot D5). */
  domaines?: PayrollDomainState[];
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
