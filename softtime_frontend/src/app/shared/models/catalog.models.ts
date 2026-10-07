import { SageDb } from './auth.models';

export type { SageDb };

export interface PointeuseDb {
  id: number;
  serveur?: string | null;
  login?: string | null;
  password?: string | null;
  sqlAuth?: boolean | null;
  nomBd?: string | null;
  typePointage?: string | null;
  active?: boolean | null;
  typeBase?: string; // 'STANDARD' | 'AUTRE'
  mapUserTable?: string | null;
  mapUserColId?: string | null;
  mapUserColBadge?: string | null;
  mapUserColSsn?: string | null;
  mapUserColNom?: string | null;
  mapPunchTable?: string | null;
  mapPunchColUserId?: string | null;
  mapPunchColDateTime?: string | null;
  mapPunchColType?: string | null;
}

export interface ClockParam {
  id: number;
  multiPoint?: boolean | null;
}

export interface CorrespondenceMode {
  active: boolean;
}

export interface CardPaie {
  id: number;
  sageMatricule?: string | null;
  branche?: string | null;
  sageNom?: string | null;
  sagePrenom?: string | null;
  pointeuseNumero?: string | null;
  pointeuseNom?: string | null;
  sageServeur?: string | null;
  pointeuseServeur?: string | null;
  sageBdd?: string | null;
  pointeuseBdd?: string | null;
  date?: string | null;
}

export interface Category {
  id: number;
  intitule?: string | null;
  supervisorCardId: number;
  pause?: string | null;
  heuresSemaine?: number | null;
}

export interface Affectation {
  id: number;
  cardPaieId: number;
  categoryId: number;
}

export interface Tolerance {
  id: number;
  categoryId: number;
  tolerance: number;
  typesTol: boolean;
  sortie?: number | null;
}

export interface Holiday {
  id: number;
  intitule?: string | null;
  date?: string | null;
}

export interface Majoration {
  id: number;
  mojoration?: string | null;
  cotation?: number | null;
}

export interface AbsenceCode {
  id: number;
  intitule?: string | null;
  notPay?: boolean | null;
}

export interface CodeConstante {
  id: number;
  categorie: string;
  intitule?: string | null;
  codeConstante: string;
  tableCible?: string | null;
}

export interface SageConstantOption {
  code: string;
  intitule?: string | null;
}

export interface DepartementService {
  matricule: string;
  departement?: string | null;
  service?: string | null;
}

export interface DepartementServiceOptions {
  departements: string[];
  services: string[];
}

export interface ConnectionTestResult {
  success: boolean;
  message: string;
  missingTables?: string[] | null;
}

export interface PayrollWriteConfiguration {
  id: number;
  tableCible?: string | null;
  colMatricule?: string | null;
  categorie: string;
  colValeur?: string | null;
}

export interface PayrollWriteResult {
  database: string;
  typeBase: string;
  employeesUpdated: number;
  categoriesUpdated: number;
  message: string;
}
