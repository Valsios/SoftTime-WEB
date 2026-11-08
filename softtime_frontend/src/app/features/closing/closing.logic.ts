/**
 * Clôture de paie assistée : logique pure de l'écran (sans Angular), testée par closing.logic.spec.ts.
 * L'écran n'invente rien : il met en forme la réponse de GET api/closing/analysis.
 */
import { ClosingComparison, ClosingCount, ClosingReport, ClosingStatus } from '../../shared/models';

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

/** Nom affiché : « Nom Prénom », ou le matricule seul quand les noms ne sont pas renvoyés. */
export function personLabel(a: { matricule: string; nom: string | null; prenom: string | null }): string {
  const nom = [a.nom, a.prenom].filter((x) => !!x && x.trim()).join(' ');
  return nom || a.matricule;
}

/** Réponse 404 « désactivée » de la route : la fonction est coupée côté serveur. */
export function isDisabledError(err: { status?: number; error?: unknown } | null | undefined): boolean {
  if (!err || err.status !== 404) return false;
  const body = err.error as { error?: unknown; message?: unknown } | null | undefined;
  const message = body?.error ?? body?.message;
  return typeof message === 'string' && message.toLowerCase().includes('désactivée');
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
