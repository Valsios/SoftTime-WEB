/**
/** Paramètres standards d'une requête paginée côté serveur. */
export interface PageQuery {
  page?: number | null;
  pageSize?: number | null;
}

/** Contrat commun d'une réponse paginée côté backend. */
export interface PagedResult<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

/** Événement émis lors d'un changement de page ou de taille de page dans le DataTable. */
export interface PageChangeEvent {
  page: number;
  pageSize: number;
}
