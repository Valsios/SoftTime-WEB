/**
 * Mon espace salarié : logique pure (sans Angular), testée par self-space.logic.spec.ts.
 * Rien ici ne désigne un autre salarié : l'écran ne montre que les données du compte connecté, le serveur étant la vraie barrière.
 */

export const SELF_PERMISSION = 'SELF:READ';
export const SELF_ROUTE = '/mon-espace';
export const DEFAULT_ROUTE = '/dashboard';
/** Fenêtre consultable : les 4 derniers mois, mois en cours compris (même règle que le serveur). */
export const WINDOW_MONTHS = 4;

export function hasSelfPermission(permissions: readonly string[] | null | undefined): boolean {
  return (permissions ?? []).includes(SELF_PERMISSION);
}

/** Profil « Salarié » : la permission SELF:READ et aucune porte. Tout autre compte garde son comportement habituel. */
export function isSelfServiceProfile(
  permissions: readonly string[] | null | undefined,
  gates: readonly number[] | null | undefined,
): boolean {
  return hasSelfPermission(permissions) && (gates ?? []).length === 0;
}

/** Page d'arrivée après la connexion : Mon espace pour un profil Salarié, le tableau de bord pour tous les autres. */
export function homeRoute(permissions: readonly string[] | null | undefined, gates: readonly number[] | null | undefined): string {
  return isSelfServiceProfile(permissions, gates) ? SELF_ROUTE : DEFAULT_ROUTE;
}

export interface MonthOption {
  /** AAAA-MM */
  value: string;
  label: string;
  /** AAAA-MM-JJ */
  from: string;
  /** AAAA-MM-JJ : fin du mois, ou aujourd'hui pour le mois en cours (aucun jour futur). */
  to: string;
}

function iso(d: Date): string {
  const p = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}`;
}

/** Les 4 derniers mois, le mois en cours en premier. */
export function monthOptions(now: Date = new Date()): MonthOption[] {
  const formatter = new Intl.DateTimeFormat('fr-FR', { month: 'long', year: 'numeric' });
  const today = new Date(now.getFullYear(), now.getMonth(), now.getDate());
  return Array.from({ length: WINDOW_MONTHS }, (_, i) => {
    const first = new Date(now.getFullYear(), now.getMonth() - i, 1);
    const last = new Date(now.getFullYear(), now.getMonth() - i + 1, 0);
    const label = formatter.format(first);
    return {
      value: iso(first).slice(0, 7),
      label: label.charAt(0).toUpperCase() + label.slice(1),
      from: iso(first),
      to: iso(i === 0 ? today : last),
    };
  });
}

/** « 7,5 h » ; vide devient « 0 h ». */
export function hoursText(value: number | null | undefined): string {
  const n = value ?? 0;
  return `${n.toLocaleString('fr-FR', { maximumFractionDigits: 2 })} h`;
}

/** AAAA-MM-JJ… → JJ/MM/AAAA. */
export function dayText(value: string | null | undefined): string {
  const m = /^(\d{4})-(\d{2})-(\d{2})/.exec(value ?? '');
  return m ? `${m[3]}/${m[2]}/${m[1]}` : '—';
}

/** HH:mm:ss → HH:mm ; vide → « — ». */
export function timeText(value: string | null | undefined): string {
  const m = /^(\d{1,2}):(\d{2})/.exec(value ?? '');
  return m ? `${m[1].padStart(2, '0')}:${m[2]}` : '—';
}

export function minutesText(minutes: number | null | undefined): string {
  return `${minutes ?? 0} min`;
}

export const EMPTY_WEEKS = 'Aucune semaine validée sur cette période.';
export const EMPTY_EXO_IMPO = 'Aucun calcul EXO / IMPO sur cette période.';
export const EMPTY_ANOMALIES = 'Aucune anomalie sur cette période.';
export const EMPTY_ABSENCES = 'Aucune absence ni aucun congé sur cette période.';
export const EMPTY_DELAYS = 'Aucun retard sur cette période.';
export const LOADING = 'Chargement…';
export const STRADDLE_NOTE =
  'Une semaine à cheval sur deux mois apparaît dans chacun de ces mois : les totaux de mois différents ne s’additionnent donc pas.';
export const CARD_ERROR = 'Ces données n’ont pas pu être chargées.';
export const SCOPE_NOTE =
  'Vos données personnelles, en lecture seule. Les heures ne comptent que les semaines validées ; les congés viennent de SAGE.';
