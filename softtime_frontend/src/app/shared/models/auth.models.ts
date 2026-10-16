import type { SourceEntityMapping } from './mapping.models';

export interface SageDb {
  id: number;
  serveur?: string | null;
  login?: string | null;
  password?: string | null;
  sqlAuth?: boolean | null;
  nomBd?: string | null;
  typeBase?: string; // 'STANDARD' | 'AUTRE'
  mappings?: SourceEntityMapping[] | null;
}

export interface LoginRequest {
  login: string;
  password: string;
}

export interface LoginResponse {
  token: string;
  userId: number;
  login: string;
  nom: string;
  roleId: number;
  matricule?: string | null;
  rights: number[];
  authorizedDatabases: SageDb[];
  permissions?: string[];
  permissionsCustomized?: boolean;
}

export interface User {
  id: number;
  roleId: number;
  nom: string;
  login: string;
  matricule?: string | null;
  password?: string | null;
  /** Adresse d'envoi des identifiants (facultative). */
  email?: string | null;
}

/** Création du premier compte administrateur (installation vierge). */
export interface AdminSetupRequest {
  nom: string;
  login: string;
  password: string;
  email?: string | null;
}

/** Résultat de la création du compte initial. */
export interface AdminSetupResult {
  userId: number;
  login: string;
  emailEnvoye: boolean;
  message: string;
}

export interface Role {
  id: number;
  nom: string;
}

export interface Droit {
  id: number;
  nom: string;
}

export interface Privilege {
  id: number;
  roleId: number;
  droitId: number;
}

export interface DbAccess {
  id: number;
  userId: number;
  sageDbId: number;
}
