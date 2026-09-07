export interface SageDb {
  id: number;
  serveur?: string | null;
  login?: string | null;
  password?: string | null;
  sqlAuth?: boolean | null;
  nomBd?: string | null;
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
}

export interface User {
  id: number;
  roleId: number;
  nom: string;
  login: string;
  matricule?: string | null;
  password?: string | null;
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
