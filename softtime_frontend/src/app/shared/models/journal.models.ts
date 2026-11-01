/** Ligne du journal central des modifications (GET api/journal). Les dates sont en UTC. */
export interface JournalEntry {
  id: number;
  dateUtc: string;
  table: string;
  key: string | null;
  column: string | null;
  action: string;
  before: string | null;
  after: string | null;
  user: string;
  sageDatabase: string | null;
  pointeuseDatabase: string | null;
  correlation: string;
  source: string;
}

export interface JournalPage {
  items: JournalEntry[];
  total: number;
  page: number;
  pageSize: number;
}

/** Valeurs distinctes de la période, pour alimenter les listes de filtres. */
export interface JournalFacets {
  tables: string[];
  actions: string[];
  users: string[];
}

/** Paramètres de GET api/journal (dates en ISO 8601 UTC avec Z). */
export interface JournalQueryParams {
  from?: string;
  to?: string;
  user?: string;
  action?: string;
  table?: string;
  key?: string;
  correlation?: string;
  sageDatabase?: string;
  page?: number;
  pageSize?: number;
}
