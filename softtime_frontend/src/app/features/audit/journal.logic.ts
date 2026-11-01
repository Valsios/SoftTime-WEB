/**
 * Logique pure de l'onglet « Journal » (sans Angular), testée par journal.logic.spec.ts :
 * paramètres de requête, dates locales/UTC, regroupement d'une opération, affichage des valeurs, export CSV.
 */
import { JournalEntry, JournalQueryParams } from '../../shared/models';

export const PAGE_SIZES = [25, 50, 100, 200] as const;
export const DEFAULT_PAGE_SIZE = 50;
export const DEFAULT_PERIOD_DAYS = 7;
export const EXPORT_BLOCK_SIZE = 200;
export const EXPORT_MAX_ROWS = 5000;
export const EXPORT_LIMIT_MESSAGE = 'Export limité à 5000 lignes. Affinez les filtres.';
export const EMPTY_MESSAGE = 'Aucune modification trouvée pour ces filtres.';
export const MASKED_VALUE = '********';
export const EMPTY_VALUE = '—';
export const VALUE_PREVIEW_LENGTH = 80;

export interface JournalFilters {
  /** Date locale de début (YYYY-MM-DD) ; le jour commence à 00:00 locale. */
  from: string;
  /** Date locale de fin (YYYY-MM-DD) ; le jour finit à 23:59:59.999 locale. */
  to: string;
  user: string;
  action: string;
  table: string;
  key: string;
  correlation: string;
  sageDatabase: string;
}

function pad(n: number): string {
  return String(n).padStart(2, '0');
}

export function toLocalIsoDate(d: Date): string {
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}

/** Filtres par défaut : les 7 derniers jours (de J-7 à aujourd'hui, heure locale). */
export function defaultFilters(now: Date = new Date()): JournalFilters {
  const start = new Date(now.getFullYear(), now.getMonth(), now.getDate() - DEFAULT_PERIOD_DAYS);
  return {
    from: toLocalIsoDate(start),
    to: toLocalIsoDate(now),
    user: '',
    action: '',
    table: '',
    key: '',
    correlation: '',
    sageDatabase: '',
  };
}

function parseLocalParts(date: string): [number, number, number] | null {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(date.trim());
  if (!match) return null;
  return [Number(match[1]), Number(match[2]), Number(match[3])];
}

/** Début du jour local → instant UTC en ISO 8601 avec Z, ou null si la date est vide/invalide. */
export function localDayStartToUtcIso(date: string): string | null {
  const parts = parseLocalParts(date ?? '');
  if (!parts) return null;
  return new Date(parts[0], parts[1] - 1, parts[2], 0, 0, 0, 0).toISOString();
}

/** Fin du jour local (23:59:59.999) → instant UTC en ISO 8601 avec Z. */
export function localDayEndToUtcIso(date: string): string | null {
  const parts = parseLocalParts(date ?? '');
  if (!parts) return null;
  return new Date(parts[0], parts[1] - 1, parts[2], 23, 59, 59, 999).toISOString();
}

/** Les listes déroulantes renvoient null quand on efface le choix : tout redevient une chaîne. */
export function normalizeFilters(f: Partial<Record<keyof JournalFilters, string | null | undefined>>): JournalFilters {
  const s = (v: string | null | undefined) => (v ?? '').trim();
  return {
    from: s(f.from),
    to: s(f.to),
    user: s(f.user),
    action: s(f.action),
    table: s(f.table),
    key: s(f.key),
    correlation: s(f.correlation),
    sageDatabase: s(f.sageDatabase),
  };
}

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export function isGuid(value: string): boolean {
  return GUID.test(value.trim());
}

/** Message d'erreur de saisie, ou null si les filtres sont acceptables. */
export function validateFilters(f: JournalFilters): string | null {
  if (f.correlation.trim() && !isGuid(f.correlation)) return 'La corrélation doit être un identifiant GUID valide.';
  const from = localDayStartToUtcIso(f.from);
  const to = localDayEndToUtcIso(f.to);
  if (from && to && from > to) return 'La date de début doit précéder la date de fin.';
  return null;
}

/** Paramètres de GET api/journal : valeurs vides ignorées, dates locales converties en UTC. */
export function buildQueryParams(filters: JournalFilters, page: number, pageSize: number): JournalQueryParams {
  const params: JournalQueryParams = {};
  const from = localDayStartToUtcIso(filters.from);
  const to = localDayEndToUtcIso(filters.to);
  if (from) params.from = from;
  if (to) params.to = to;
  const text = (value: string) => value.trim();
  if (text(filters.user)) params.user = text(filters.user);
  if (text(filters.action)) params.action = text(filters.action);
  if (text(filters.table)) params.table = text(filters.table);
  if (text(filters.key)) params.key = text(filters.key);
  if (text(filters.correlation)) params.correlation = text(filters.correlation);
  if (text(filters.sageDatabase)) params.sageDatabase = text(filters.sageDatabase);
  params.page = Math.max(1, Math.floor(page) || 1);
  const size = Math.floor(pageSize) || DEFAULT_PAGE_SIZE;
  params.pageSize = Math.min(Math.max(size, 1), EXPORT_BLOCK_SIZE);
  return params;
}

/** Les dates du serveur sont en UTC mais sérialisées sans « Z » : on le suppose pour ne pas les lire en heure locale. */
export function parseUtcDate(value: string): Date {
  const hasZone = /(Z|[+-]\d{2}:?\d{2})$/i.test(value);
  return new Date(hasZone ? value : `${value}Z`);
}

/** Date UTC du serveur affichée en heure locale (jj/mm/aaaa hh:mm:ss). */
export function formatLocalDateTime(value: string | null | undefined): string {
  if (!value) return EMPTY_VALUE;
  const d = parseUtcDate(value);
  if (Number.isNaN(d.getTime())) return value;
  return `${pad(d.getDate())}/${pad(d.getMonth() + 1)}/${d.getFullYear()} ${pad(d.getHours())}:${pad(d.getMinutes())}:${pad(d.getSeconds())}`;
}

export function totalPages(total: number, pageSize: number): number {
  return Math.max(1, Math.ceil(Math.max(0, total) / Math.max(1, pageSize)));
}

/** Résumé de la colonne concernée : son nom, sinon l'ouverture du texte de synthèse. */
export function columnSummary(entry: JournalEntry, max = 60): string {
  if (entry.column) return entry.column;
  const text = entry.after ?? entry.before;
  if (!text) return EMPTY_VALUE;
  return text.length > max ? `${text.slice(0, max)}…` : text;
}

/** Deux lignes appartiennent à la même opération : même corrélation, même table, même clé. */
export function sameOperation(a: JournalEntry, b: JournalEntry): boolean {
  return a.correlation === b.correlation && a.table === b.table && (a.key ?? null) === (b.key ?? null);
}

export interface OperationGroup {
  correlation: string;
  table: string;
  key: string | null;
  action: string;
  rows: JournalEntry[];
}

/** Regroupe des lignes par (corrélation, table, clé) en gardant l'ordre de première apparition. */
export function groupOperations(rows: readonly JournalEntry[]): OperationGroup[] {
  const groups = new Map<string, OperationGroup>();
  for (const row of rows) {
    const id = JSON.stringify([row.correlation, row.table, row.key ?? null]);
    const group = groups.get(id);
    if (group) group.rows.push(row);
    else groups.set(id, { correlation: row.correlation, table: row.table, key: row.key ?? null, action: row.action, rows: [row] });
  }
  return [...groups.values()];
}

export type ValueKind = 'masked' | 'empty' | 'value';

export interface DisplayValue {
  kind: ValueKind;
  text: string;
  /** Mention lisible sans la couleur, pour les valeurs masquées. */
  note: string | null;
}

/** Valeur affichée : masquée (********) conservée telle quelle avec la mention « masqué », vide → « — ». */
export function displayValue(value: string | null | undefined): DisplayValue {
  if (value === null || value === undefined || value === '') return { kind: 'empty', text: EMPTY_VALUE, note: null };
  if (value === MASKED_VALUE) return { kind: 'masked', text: MASKED_VALUE, note: 'masqué' };
  return { kind: 'value', text: value, note: null };
}

export function truncateValue(text: string, max = VALUE_PREVIEW_LENGTH): { text: string; truncated: boolean } {
  return text.length > max ? { text: `${text.slice(0, max)}…`, truncated: true } : { text, truncated: false };
}

export interface DetailRow {
  id: number;
  column: string;
  before: DisplayValue;
  after: DisplayValue;
}

/** Tableau « Colonne | Avant | Après » d'une opération, dans l'ordre d'écriture (identifiant croissant). */
export function buildDetailRows(rows: readonly JournalEntry[]): DetailRow[] {
  return [...rows]
    .sort((a, b) => a.id - b.id)
    .map((r) => ({
      id: r.id,
      column: r.column ?? 'Opération entière',
      before: displayValue(r.before),
      after: displayValue(r.after),
    }));
}

// ---------- Export CSV ----------

export const CSV_SEPARATOR = ';';
export const CSV_BOM = '﻿';
export const CSV_HEADERS = [
  'Date (heure locale)',
  'Utilisateur',
  'Action',
  'Table',
  'Clé',
  'Colonne',
  'Avant',
  'Après',
  'Base SAGE',
  'Base pointeuse',
  'Corrélation',
  'Source',
] as const;

const FORMULA_START = /^[=+\-@\t\r]/;

/**
 * Cellule CSV : préfixe d'une apostrophe si elle commence par = + - @ tabulation ou retour chariot
 * (injection de formules), puis guillemets doublés et cellule entourée de guillemets si elle contient
 * un séparateur, un guillemet ou un saut de ligne.
 */
export function csvCell(value: unknown): string {
  let text = value === null || value === undefined ? '' : String(value);
  if (FORMULA_START.test(text)) text = `'${text}`;
  return /[;"\r\n]/.test(text) ? `"${text.replace(/"/g, '""')}"` : text;
}

export function csvLine(cells: readonly unknown[]): string {
  return cells.map(csvCell).join(CSV_SEPARATOR);
}

export function entryToCsvCells(e: JournalEntry): string[] {
  return [
    formatLocalDateTime(e.dateUtc),
    e.user,
    e.action,
    e.table,
    e.key ?? '',
    e.column ?? '',
    e.before ?? '',
    e.after ?? '',
    e.sageDatabase ?? '',
    e.pointeuseDatabase ?? '',
    e.correlation,
    e.source,
  ];
}

/** Contenu CSV (sans BOM), lignes séparées par CRLF. */
export function buildCsv(entries: readonly JournalEntry[]): string {
  return [csvLine(CSV_HEADERS), ...entries.map((e) => csvLine(entryToCsvCells(e)))].join('\r\n') + '\r\n';
}

export interface ExportPlan {
  /** Nombre de lignes réellement exportées. */
  rows: number;
  /** Nombre de blocs de EXPORT_BLOCK_SIZE à récupérer. */
  blocks: number;
  /** Vrai si le total dépasse EXPORT_MAX_ROWS (message à afficher). */
  limited: boolean;
}

export function planExport(total: number): ExportPlan {
  const rows = Math.min(Math.max(0, total), EXPORT_MAX_ROWS);
  return { rows, blocks: Math.ceil(rows / EXPORT_BLOCK_SIZE), limited: total > EXPORT_MAX_ROWS };
}

export function exportFileName(now: Date = new Date()): string {
  return `journal-audit-${now.getFullYear()}${pad(now.getMonth() + 1)}${pad(now.getDate())}-${pad(now.getHours())}${pad(now.getMinutes())}.csv`;
}
