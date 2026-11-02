/**
 * Paramètres d'adresse facultatifs des pages de correction (du, au, matricule).
 * Les valeurs ne servent qu'à remplir des filtres existants : tout ce qui est invalide est ignoré
 * en silence, rien n'est exécuté ni inséré dans le HTML.
 */
export interface DeepLinkFilter {
  from: string;
  to: string;
  matricule: string | null;
}

/** Plage maximale acceptée entre « du » et « au » (en jours). */
export const DEEP_LINK_MAX_DAYS = 31;

const ISO_DAY = /^(\d{4})-(\d{2})-(\d{2})$/;
const MATRICULE = /^[A-Za-z0-9]{1,10}$/;
const MS_PER_DAY = 86_400_000;

/** AAAA-MM-JJ strict et réel (31/02 refusé) → millisecondes UTC, sinon null. */
function isoDayToUtc(value: unknown): number | null {
  if (typeof value !== 'string') return null;
  const m = ISO_DAY.exec(value);
  if (!m) return null;
  const [y, mo, d] = [Number(m[1]), Number(m[2]), Number(m[3])];
  const time = Date.UTC(y, mo - 1, d);
  const check = new Date(time);
  return check.getUTCFullYear() === y && check.getUTCMonth() === mo - 1 && check.getUTCDate() === d ? time : null;
}

/** Valide les paramètres bruts ; null si la période est absente ou invalide. Le matricule invalide est simplement ignoré. */
export function readDeepLink(params: { du?: unknown; au?: unknown; matricule?: unknown } | null | undefined): DeepLinkFilter | null {
  if (!params) return null;
  const from = isoDayToUtc(params.du);
  const to = isoDayToUtc(params.au);
  if (from === null || to === null || from > to || (to - from) / MS_PER_DAY > DEEP_LINK_MAX_DAYS) return null;
  const raw = typeof params.matricule === 'string' ? params.matricule.trim() : '';
  return {
    from: params.du as string,
    to: params.au as string,
    matricule: MATRICULE.test(raw) ? raw : null,
  };
}

/** Même règle que MatriculeIdentity.Cle côté serveur : zéros retirés si numérique, sinon majuscules. */
export function matriculeKey(value: string): string {
  const v = value.trim();
  return /^\d+$/.test(v) ? v.replace(/^0+(?=\d)/, '') : v.toUpperCase();
}

/** Valeur de l'option dont la clé égale celle du matricule demandé ; null si aucune (aucun filtre matricule). */
export function matchMatricule(cle: string, options: readonly { value: string }[]): string | null {
  const wanted = matriculeKey(cle);
  if (!wanted) return null;
  return options.find((o) => matriculeKey(o.value) === wanted)?.value ?? null;
}
