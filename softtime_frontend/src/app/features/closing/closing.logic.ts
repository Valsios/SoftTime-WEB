/**
 * Clôture de paie assistée : logique pure de l'écran (sans Angular), testée par closing.logic.spec.ts.
 * L'écran n'invente rien : il met en forme la réponse de GET api/closing/analysis.
 */
import { ClosingAnomalyView, ClosingChecklistLine, ClosingComparison, ClosingCount, ClosingReport, ClosingStatus } from '../../shared/models';
import type { ExcelSheet } from '../../shared/services/excel-export.service';

export const DEFAULT_PAGE_SIZE = 50;
export const NO_CONTROLLED_DAY = 'Aucun jour contrôlé sur cette période';
export const NO_ANOMALY = 'Aucune anomalie détectée : les données de la période sont prêtes pour la clôture.';
export const NO_ANOMALY_TO_FIX = 'Aucune anomalie à corriger : les données de la période sont prêtes pour la clôture.';
export const NOTHING_TO_FIX = 'Rien à corriger dans l’application';
/** Rôle du niveau « Information » dans l'explication dépliable du score et du statut. */
export const INFORMATION_RULE =
  'Une anomalie « Information » n’a pas d’effet sur la paie : elle figure dans les comptages et, si la case est cochée, dans la liste, mais elle n’entre ni dans le score (poids 0 par défaut) ni dans le statut.';
export const DISABLED_MESSAGE = 'Le contrôle avant paie est désactivé sur ce serveur (réglage Closing:Enabled).';
export const PURPOSE_NOTE = 'Outil d’aide à la clôture : à utiliser pour corriger les données, pas pour sanctionner.';

/** Ordre d'affichage des types, identique à l'énumération du serveur. */
export const ANOMALY_TYPES = [
  'PointageIncomplet',
  'PointageManquant',
  'PlanningSansPointage',
  'PointageSansPlanning',
  'DoublonExact',
  'QuasiDoublon',
  'FerieNonApplique',
] as const;

export const SEVERITIES = ['Critique', 'Importante', 'Mineure', 'Information'] as const;

interface TypeLabel {
  singulier: string;
  pluriel: string;
}

const TYPE_LABELS: Record<string, TypeLabel> = {
  PointageIncomplet: { singulier: 'pointage incomplet', pluriel: 'pointages incomplets' },
  PointageManquant: { singulier: 'pointage manquant', pluriel: 'pointages manquants' },
  PlanningSansPointage: { singulier: 'planning sans pointage', pluriel: 'plannings sans pointage' },
  PointageSansPlanning: { singulier: 'pointage sans planning', pluriel: 'pointages sans planning' },
  DoublonExact: { singulier: 'doublon exact', pluriel: 'doublons exacts' },
  QuasiDoublon: { singulier: 'quasi-doublon', pluriel: 'quasi-doublons' },
  FerieNonApplique: { singulier: 'férié non appliqué', pluriel: 'fériés non appliqués' },
};

const SEVERITY_LABELS: Record<string, TypeLabel> = {
  Critique: { singulier: 'critique', pluriel: 'critiques' },
  Importante: { singulier: 'importante', pluriel: 'importantes' },
  Mineure: { singulier: 'mineure', pluriel: 'mineures' },
  Information: { singulier: 'information', pluriel: 'informations' },
};

export type BadgeTone = 'success' | 'warning' | 'danger' | 'info' | 'muted';

function capitalize(text: string): string {
  return text ? text.charAt(0).toUpperCase() + text.slice(1) : text;
}

/** AAAA-MM-JJ d'une date locale. */
export function toIsoDay(d: Date): string {
  const p = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}`;
}

/** Période par défaut : mois civil complet PRÉCÉDENT (même règle que le serveur). */
export function defaultPeriod(now: Date = new Date()): { from: string; to: string } {
  const debut = new Date(now.getFullYear(), now.getMonth() - 1, 1);
  const fin = new Date(now.getFullYear(), now.getMonth(), 0);
  return { from: toIsoDay(debut), to: toIsoDay(fin) };
}

/** Libellé du type (« Pointage incomplet ») ; nombre donné : accord singulier/pluriel. */
export function typeLabel(type: string, count = 1): string {
  const l = TYPE_LABELS[type];
  if (!l) return type;
  return capitalize(Math.abs(count) > 1 ? l.pluriel : l.singulier);
}

export function severityLabel(severity: string): string {
  return SEVERITY_LABELS[severity] ? capitalize(SEVERITY_LABELS[severity].singulier) : severity;
}

export function severityTone(severity: string): BadgeTone {
  switch (severity) {
    case 'Critique':
      return 'danger';
    case 'Importante':
      return 'warning';
    case 'Mineure':
      return 'info';
    default:
      return 'muted';
  }
}

/** Statut : toujours un texte (jamais la couleur seule) et une nuance de badge. */
export function statusView(statut: ClosingStatus | string | null | undefined): { label: string; tone: BadgeTone; hint: string } {
  switch (statut) {
    case 'Pret':
      return { label: 'Prêt', tone: 'success', hint: 'Aucune anomalie critique.' };
    case 'ACorriger':
      return { label: 'À corriger', tone: 'warning', hint: 'Des anomalies critiques restent à corriger.' };
    case 'Bloquant':
      return { label: 'Bloquant', tone: 'danger', hint: 'Trop d’anomalies critiques pour clôturer.' };
    default:
      return { label: 'Sans statut', tone: 'muted', hint: NO_CONTROLLED_DAY + '.' };
  }
}

/** Score affiché : « 87 / 100 », ou le message « aucun jour contrôlé » (jamais 0 ni 100 inventé). */
export function scoreText(score: number | null | undefined): string {
  return score === null || score === undefined ? NO_CONTROLLED_DAY : `${score} / 100`;
}

/** Salariés sans aucun planning sur la période, non contrôlés (vide quand il n'y en a pas). */
export function excludedEmployeesText(count: number | null | undefined): string {
  const n = count ?? 0;
  if (n <= 0) return '';
  return n > 1
    ? `${n} salariés sans aucun planning sur la période ne sont pas contrôlés.`
    : '1 salarié sans aucun planning sur la période n’est pas contrôlé.';
}

/** Règle du statut en clair, avec le seuil renvoyé par le serveur. */
export function statusRuleText(seuilBloquant: number): string {
  const pct = Math.round(seuilBloquant * 10000) / 100;
  return `Prêt : aucune anomalie critique. Bloquant : anomalies critiques au moins égales à ${pct.toLocaleString('fr-FR')} % des jours-employé contrôlés. Sinon : à corriger.`;
}

export function countOf(counts: readonly ClosingCount[] | null | undefined, key: string): number {
  return counts?.find((c) => c.cle === key)?.nombre ?? 0;
}

/** « 1 anomalie critique », « 3 anomalies critiques », « Aucune anomalie critique ». */
export function severitySentence(severity: string, count: number): string {
  if (severity === 'Information') {
    if (count <= 0) return 'Aucune information';
    return `${count} ${count > 1 ? 'informations' : 'information'} sans effet sur le score`;
  }
  const l = SEVERITY_LABELS[severity];
  const adjectif = l ? (count > 1 ? l.pluriel : l.singulier) : severity;
  if (count <= 0) return `Aucune anomalie ${adjectif}`;
  return `${count} ${count > 1 ? 'anomalies' : 'anomalie'} ${adjectif}`;
}

function comparisonTail(delta: number): string {
  if (delta > 0) return `, ${delta} de plus que la période précédente`;
  if (delta < 0) return `, ${-delta} de moins que la période précédente`;
  return ', autant que la période précédente';
}

/**
 * Résumé en phrases par gabarits locaux (aucun appel externe). Sans comparaison : aucune phrase
 * de comparaison. Sans jour contrôlé : une seule phrase.
 */
export function summarySentences(
  report: Pick<ClosingReport, 'score' | 'joursEmployeControles' | 'nombreAnomalies' | 'parType' | 'comparaison'> &
    Partial<Pick<ClosingReport, 'parGravite' | 'salariesSansPlanningExclus'>>,
): string[] {
  if (report.score === null || report.score === undefined || report.joursEmployeControles <= 0) return [NO_CONTROLLED_DAY + '.'];

  const comparaison: ClosingComparison | null = report.comparaison ?? null;
  const delta = (type: string) => comparaison?.parType.find((t) => t.type === type) ?? null;
  const phrases: string[] = [];

  // Les « Information » ne sont pas des anomalies à corriger : aucune phrase d'alerte pour elles.
  const informations = report.parGravite ? countOf(report.parGravite, 'Information') : 0;
  if (report.nombreAnomalies <= 0) phrases.push(NO_ANOMALY);
  else if (informations > 0 && report.nombreAnomalies - informations <= 0) phrases.push(NO_ANOMALY_TO_FIX);
  for (const type of ANOMALY_TYPES) {
    const n = countOf(report.parType, type);
    const d = delta(type);
    if (n > 0) {
      phrases.push(`${n} ${typeLabel(type, n).toLowerCase()}${d ? comparisonTail(d.ecart) : ''}.`);
    } else if (d && d.precedent > 0) {
      phrases.push(`Plus aucun ${TYPE_LABELS[type].singulier} (${d.precedent} sur la période précédente).`);
    }
  }

  if (informations > 0) {
    phrases.push(
      `${severitySentence('Information', informations)} : elles ne comptent ni dans le score ni dans le statut.`,
    );
  }
  const exclus = excludedEmployeesText(report.salariesSansPlanningExclus);
  if (exclus) phrases.push(exclus);

  if (comparaison?.ecartScore !== null && comparaison?.ecartScore !== undefined) {
    const e = comparaison.ecartScore;
    const points = Math.abs(e) > 1 ? 'points' : 'point';
    phrases.push(
      e > 0
        ? `Score en hausse de ${e} ${points} par rapport à la période précédente.`
        : e < 0
          ? `Score en baisse de ${-e} ${points} par rapport à la période précédente.`
          : 'Score identique à celui de la période précédente.',
    );
  }
  return phrases;
}

/** Page de correction d'un type d'anomalie, avec le droit qui la protège (même règle que le menu). */
export interface CorrectionTarget {
  path: string;
  label: string;
  permission: string;
  droit: number;
}

const DROIT_PROCESSING = 3;

export const CORRECTION_TARGETS: Record<string, CorrectionTarget> = {
  PointageIncomplet: { path: '/anomalies', label: 'Anomalies', permission: 'PROC:ANOMALIES', droit: DROIT_PROCESSING },
  PointageManquant: { path: '/corrected-hours', label: 'Heures corrigées', permission: 'PROC:CORRECTED_HOURS', droit: DROIT_PROCESSING },
  PlanningSansPointage: { path: '/corrected-hours', label: 'Heures corrigées', permission: 'PROC:CORRECTED_HOURS', droit: DROIT_PROCESSING },
  PointageSansPlanning: { path: '/employee-planning', label: 'Planning salariés', permission: 'PROC:EMPLOYEE_PLANNING', droit: DROIT_PROCESSING },
  DoublonExact: { path: '/punches', label: 'Pointages', permission: 'PROC:PUNCHES', droit: DROIT_PROCESSING },
  QuasiDoublon: { path: '/punches', label: 'Pointages', permission: 'PROC:PUNCHES', droit: DROIT_PROCESSING },
  // Le drapeau férié absent n'a pas d'effet sur la paie : seul un pointage ce jour-là se vérifie (voir correctionLink).
  FerieNonApplique: { path: '/anomalies', label: 'Anomalies', permission: 'PROC:ANOMALIES', droit: DROIT_PROCESSING },
};

/**
 * Pages cibles qui lisent les paramètres facultatifs du, au et matricule (entrées liées à la route,
 * validées par readDeepLink). Les autres pages (Fériés) ouvrent sans paramètres.
 */
export const PAGES_WITH_QUERY_PARAMS: ReadonlySet<string> = new Set<string>([
  '/anomalies',
  '/corrected-hours',
  '/employee-planning',
  '/punches',
]);

/** Types dont la plage va jusqu'au lendemain (sortie d'un poste de nuit ou doublon à cheval sur minuit). */
const TYPES_JUSQU_AU_LENDEMAIN: ReadonlySet<string> = new Set(['PointageIncomplet', 'DoublonExact', 'QuasiDoublon', 'FerieNonApplique']);

/** AAAA-MM-JJ → jour suivant (fin de mois et d'année comprises) ; vide si la date n'est pas lisible. */
export function jourSuivant(jour: string): string {
  const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(jour);
  if (!m) return '';
  const d = new Date(Date.UTC(Number(m[1]), Number(m[2]) - 1, Number(m[3]) + 1));
  return d.toISOString().slice(0, 10);
}

export interface CorrectionLink extends CorrectionTarget {
  queryParams: Record<string, string> | null;
  ariaLabel: string;
}

/** Férié non appliqué sans pointage ce jour-là : rien à corriger dans l'application (pas de lien, pas d'erreur). */
export function nothingToFix(anomaly: { type: string; pointageExiste?: boolean | null }): boolean {
  return anomaly.type === 'FerieNonApplique' && anomaly.pointageExiste !== true;
}

export function correctionLink(
  anomaly: { type: string; matricule: string; date: string; pointageExiste?: boolean | null },
  pagesWithParams: ReadonlySet<string> = PAGES_WITH_QUERY_PARAMS,
): CorrectionLink | null {
  const target = CORRECTION_TARGETS[anomaly.type];
  if (!target || nothingToFix(anomaly)) return null;
  const jour = anomaly.date ? anomaly.date.slice(0, 10) : '';
  return {
    ...target,
    queryParams: pagesWithParams.has(target.path) && jour
      ? { du: jour, au: TYPES_JUSQU_AU_LENDEMAIN.has(anomaly.type) ? jourSuivant(jour) : jour, matricule: anomaly.matricule }
      : null,
    ariaLabel: `Corriger dans ${target.label} : ${typeLabel(anomaly.type).toLowerCase()}, matricule ${anomaly.matricule}, ${formatDay(jour)}`,
  };
}

/** AAAA-MM-JJ… → JJ/MM/AAAA. */
export function formatDay(value: string | null | undefined): string {
  if (!value) return '—';
  const m = /^(\d{4})-(\d{2})-(\d{2})/.exec(value);
  return m ? `${m[3]}/${m[2]}/${m[1]}` : value;
}

/** Filtre d'une analyse : période, département et option de comparaison (l'onglet et la page n'en font pas partie). */
export interface ClosingFilter {
  from: string;
  to: string;
  departement: string | null;
  includeComparison: boolean;
}

export const ANALYSING_TEXT = 'Analyse en cours…';
export const NOT_ANALYSED_TEXT = 'Choisissez la période puis cliquez sur « Analyser » : aucune analyse ne se lance toute seule.';
export const FILTER_CHANGED_TEXT = 'Filtre modifié, réanalyser : le résultat affiché correspond encore à l’ancien filtre.';

/** Libellé et teinte de l'état d'une étape de la checklist. */
export function checklistStateView(etat: string): { label: string; tone: BadgeTone } {
  switch (etat) {
    case 'Fait':
      return { label: 'Fait', tone: 'success' };
    case 'AFaire':
      return { label: 'À faire', tone: 'warning' };
    case 'Information':
      return { label: 'Information', tone: 'info' };
    default:
      return { label: 'Non déterminable', tone: 'muted' };
  }
}

/** Badge de la barre du haut : nombre d'anomalies critiques du dernier résultat, avec un libellé lisible. */
export function closingBadgeView(report: Pick<ClosingReport, 'parGravite'>): { count: number; label: string } {
  const count = countOf(report.parGravite, 'Critique');
  const texte = count === 0 ? 'aucune anomalie critique' : count === 1 ? '1 anomalie critique' : `${count} anomalies critiques`;
  return { count, label: `Contrôle avant paie : ${texte}` };
}

export const CHECKLIST_TITLE = 'Avancement de la paie';
export const CHECKLIST_LOADING = 'Avancement de la paie en cours de chargement…';
export const CHECKLIST_ERROR = 'L’avancement de la paie n’a pas pu être chargé. L’analyse reste valable.';
export const CHECKLIST_NOTE = 'Repères de préparation, sans effet sur le score ni sur le statut.';

export interface ChecklistLink extends CorrectionTarget {
  queryParams: Record<string, string> | null;
}

/** Écran existant de chaque étape ; la période n'est transmise qu'aux écrans qui la lisent (du, au). */
const CHECKLIST_TARGETS: Record<string, CorrectionTarget> = {
  Pointages: { path: '/punches', label: 'Pointages', permission: 'PROC:PUNCHES', droit: DROIT_PROCESSING },
  Anomalies: { path: '/anomalies', label: 'Anomalies', permission: 'PROC:ANOMALIES', droit: DROIT_PROCESSING },
  Semaines: { path: '/weekly-validation', label: 'Validation semaine', permission: 'PROC:WEEKLY_VALIDATION', droit: DROIT_PROCESSING },
  ExoImpo: { path: '/overtime', label: 'Heures supp.', permission: 'PROC:OVERTIME', droit: DROIT_PROCESSING },
  HeuresImposables: { path: '/overtime', label: 'Heures supp.', permission: 'PROC:OVERTIME', droit: DROIT_PROCESSING },
  SynchroPaie: { path: '/overtime', label: 'Heures supp.', permission: 'PROC:OVERTIME', droit: DROIT_PROCESSING },
};

export function checklistLink(line: Pick<ClosingChecklistLine, 'code'>, filter: { from: string; to: string }): ChecklistLink | null {
  const target = CHECKLIST_TARGETS[line.code];
  if (!target) return null;
  return {
    ...target,
    queryParams: PAGES_WITH_QUERY_PARAMS.has(target.path) ? { du: filter.from, au: filter.to } : null,
  };
}

/** Tri de la liste : ordre habituel (date, matricule, type) ou gravité décroissante. */
export type ClosingSort = 'date' | 'gravite';

/** Ce que montre la liste : niveaux retenus (vide : aucun filtre), tri et présence des « Information » (onglet). */
export interface ClosingListView {
  gravites: readonly string[];
  tri: ClosingSort;
  includeInformation: boolean;
}

/** Ajoute ou retire un niveau ; le résultat suit toujours l'ordre d'affichage des cartes (du plus grave au moins grave). */
export function toggleSeverity(current: readonly string[], severity: string): string[] {
  const next = current.includes(severity) ? current.filter((s) => s !== severity) : [...current, severity];
  return SEVERITIES.filter((s) => next.includes(s));
}

/** « Filtre de gravité : Critique, Importante » ; vide sans filtre. */
export function severityFilterText(gravites: readonly string[]): string {
  return gravites.length ? `Filtre de gravité : ${gravites.map(severityLabel).join(', ')}` : '';
}

export const NO_ANOMALY_FOR_FILTER = 'Aucune anomalie pour ce filtre de gravité.';

/** Deux filtres équivalents (département vide = tous). */
export function sameFilter(a: ClosingFilter | null | undefined, b: ClosingFilter | null | undefined): boolean {
  if (!a || !b) return false;
  return a.from === b.from
    && a.to === b.to
    && (a.departement || null) === (b.departement || null)
    && a.includeComparison === b.includeComparison;
}

/** « JJ/MM à HH:mm » (heure locale). */
export function formatStamp(date: Date): string {
  const p = (n: number) => String(n).padStart(2, '0');
  return `${p(date.getDate())}/${p(date.getMonth() + 1)} à ${p(date.getHours())}:${p(date.getMinutes())}`;
}

/**
 * Âge de l'analyse en texte seul : « Analyse faite il y a 3 min ». Calculé depuis l'heure conservée en session,
 * sans aucune requête ; de 1 h à 59 min 59 s, les minutes ; au-delà, les heures puis les minutes restantes.
 */
export function analysisAgeText(analysedAt: Date, now: Date): string {
  const minutes = Math.max(0, Math.floor((now.getTime() - analysedAt.getTime()) / 60000));
  if (minutes < 1) return 'Analyse faite il y a moins d’une minute.';
  if (minutes < 60) return `Analyse faite il y a ${minutes} min.`;
  const heures = Math.floor(minutes / 60);
  const reste = minutes % 60;
  return `Analyse faite il y a ${heures} h${reste ? ` ${reste} min` : ''}.`;
}

/** Bandeau du dernier résultat : quand, pour quelle période et quel département. */
export function resultBannerText(analysedAt: Date, filter: ClosingFilter): string {
  const departement = filter.departement ? filter.departement : 'tous';
  return `Résultat du ${formatStamp(analysedAt)}, période du ${formatDay(filter.from)} au ${formatDay(filter.to)}, département ${departement}.`;
}

/** Nom affiché : « Nom Prénom », ou le matricule seul quand les noms ne sont pas renvoyés. */
export function personLabel(a: { matricule: string; nom: string | null; prenom: string | null }): string {
  const nom = [a.nom, a.prenom].filter((x) => !!x && x.trim()).join(' ');
  return nom || a.matricule;
}

/** Réponse 404 « désactivée » de la route : la fonction est coupée côté serveur. */
export function isDisabledError(err: { status?: number; error?: unknown } | null | undefined): boolean {
  if (!err || err.status !== 404) return false;
  const body = err.error as { error?: unknown } | null | undefined;
  return typeof body?.error === 'string' && body.error.toLowerCase().includes('désactivée');
}

/** Carte du tableau de bord : statut, score et critiques du mois précédent. */
export function dashboardCardView(report: Pick<ClosingReport, 'statut' | 'score' | 'parGravite' | 'joursEmployeControles'>): {
  status: { label: string; tone: BadgeTone };
  score: string;
  critiques: string;
} {
  const s = statusView(report.statut);
  return {
    status: { label: s.label, tone: s.tone },
    score: scoreText(report.score),
    critiques: report.joursEmployeControles > 0 ? severitySentence('Critique', countOf(report.parGravite, 'Critique')) : '',
  };
}

/** Taille de page utilisée pour rassembler toute la liste à exporter (maximum accepté par le serveur). */
export const EXPORT_PAGE_SIZE = 200;
export const EXPORT_STALE_MESSAGE = 'Les données ont changé depuis l’analyse : réanalysez avant d’exporter.';

/** Colonnes de la feuille « Anomalies » : celles de la liste affichée, sans la colonne de lien « Correction ». */
export const EXPORT_HEADERS = ['Gravité', 'Type', 'Matricule', 'Salarié', 'Département', 'Date', 'Détail'];

export function exportFileName(filter: ClosingFilter): string {
  return `controle-avant-paie_${filter.from}_${filter.to}`;
}

/** Lignes de la feuille « Anomalies », dans l'ordre de la liste (aucun tri ni filtre ajouté). */
export function exportRows(items: readonly ClosingAnomalyView[]): (string | number | boolean | null)[][] {
  return items.map((a) => [
    severityLabel(a.gravite),
    typeLabel(a.type),
    a.matricule,
    personLabel(a),
    a.departement ?? '',
    formatDay(a.date),
    a.detail,
  ]);
}

/** Feuille de synthèse : filtre analysé, statut, score, comptages et nombre de lignes exportées. */
export function exportSummary(
  report: ClosingReport,
  filter: ClosingFilter,
  analysedAt: Date,
  exportedRows: number,
  view: ClosingListView = { gravites: [], tri: 'date', includeInformation: false },
): ExcelSheet {
  const rows: (string | number | boolean | null)[][] = [
    ['Période', `du ${formatDay(filter.from)} au ${formatDay(filter.to)}`],
    ['Département', filter.departement ? filter.departement : 'Tous'],
    ['Comparaison avec la période précédente', filter.includeComparison ? 'Oui' : 'Non'],
    ['Analyse du', formatStamp(analysedAt)],
    ['Statut', report.statutLibelle ?? statusView(report.statut).label],
    ['Score', report.score === null ? 'Aucun jour contrôlé' : report.score],
    ['Jours-employé contrôlés', report.joursEmployeControles],
    ['Anomalies (toutes gravités)', report.nombreAnomalies],
    ['Filtre de gravité', view.gravites.length ? view.gravites.map(severityLabel).join(', ') : view.includeInformation ? 'Aucun (toutes gravités)' : 'Aucun (hors « Information »)'],
    ['Tri de la liste', view.tri === 'gravite' ? 'Gravité décroissante, puis date' : 'Date, matricule, type'],
    ['Lignes de la feuille « Anomalies »', exportedRows],
  ];
  for (const s of SEVERITIES) rows.push([`Gravité : ${severityLabel(s)}`, countOf(report.parGravite, s)]);
  for (const t of report.parType) rows.push([`Type : ${typeLabel(t.cle)}`, t.nombre]);
  return { sheetName: 'Synthèse', headers: ['Information', 'Valeur'], rows };
}

export function exportSheets(
  report: ClosingReport,
  items: readonly ClosingAnomalyView[],
  filter: ClosingFilter,
  analysedAt: Date,
  view: ClosingListView = { gravites: [], tri: 'date', includeInformation: false },
): ExcelSheet[] {
  return [
    exportSummary(report, filter, analysedAt, items.length, view),
    { sheetName: 'Anomalies', headers: EXPORT_HEADERS, rows: exportRows(items) },
  ];
}
