/** Droits qui donnent accès à la liste des comptes et des droits. */
export const SENSITIVE_RIGHTS: readonly { code: string; label: string }[] = [
  { code: 'PARAMS:USERS', label: 'Utilisateurs' },
  { code: 'PARAMS:ROLES', label: 'Rôles' },
  { code: 'PARAMS:PRIVILEGES', label: 'Privilèges' },
  { code: 'PARAMS:DB_ACCESS', label: 'Accès bases' },
];

export const SENSITIVE_RIGHT_HINT = 'Droit sensible : donne accès à la liste des comptes et des droits';

export function isSensitiveRight(code: string): boolean {
  return SENSITIVE_RIGHTS.some((r) => r.code === code);
}

/** Libellés des droits sensibles accordés par le brouillon et absents de la référence (enregistrée). */
export function sensitiveGranted(permissions: Iterable<string>, baseline: Iterable<string> = []): string[] {
  const draft = new Set(permissions);
  const saved = new Set(baseline);
  return SENSITIVE_RIGHTS.filter((r) => draft.has(r.code) && !saved.has(r.code)).map((r) => r.label);
}

/** Vrai si le brouillon accorde un droit de travail (PROC, OTHER, REPORTS) sans aucun accès en lecture à une base SAGE. */
export function hasWorkRightsWithoutBaseAccess(permissions: Iterable<string>): boolean {
  let work = false;
  for (const code of permissions) {
    if (/^SAGE:.+:READ$/.test(code)) return false;
    if (/^(PROC|OTHER|REPORTS):/.test(code)) work = true;
  }
  return work;
}

/** Avertissements affichés dans la barre de brouillon ; ils ne bloquent jamais l'enregistrement. */
export function computeWarnings(
  permissions: Iterable<string>,
  mode: 'user' | 'role',
  baseline: Iterable<string> = [],
): string[] {
  const list = [...permissions];
  const warnings: string[] = [];
  if (hasWorkRightsWithoutBaseAccess(list)) {
    const who = mode === 'user' ? 'ce compte ne verra' : 'les personnes de ce rôle ne verront';
    warnings.push(
      `Sans accès aux bases, ${who} aucune donnée dans Traitements, Rapports ou Autres. ` +
        'Ajoutez l’accès à une base (ici ou dans la page « Accès bases »), ou enregistrez ainsi si c’est voulu.',
    );
  }
  const sensitive = sensitiveGranted(list, baseline);
  if (sensitive.length > 0) warnings.push(`Droit sensible accordé : ${sensitive.join(', ')}.`);
  return warnings;
}

const ACTION_CODE = /^(.+):(CREATE|UPDATE|DELETE)$/;
const ACTION_SUFFIXES = ['CREATE', 'UPDATE', 'DELETE'];

/**
 * Coche ou décoche un droit en respectant les dépendances d'une page : cocher une action coche sa page,
 * décocher une page décoche ses actions. `pageCodes` liste les pages (les autres codes sont inchangés).
 */
export function applyToggle(
  current: ReadonlySet<string>,
  code: string,
  checked: boolean,
  pageCodes: ReadonlySet<string>,
  isMandatory: (code: string) => boolean = () => false,
): Set<string> {
  const next = new Set(current);
  if (checked) {
    next.add(code);
    const match = ACTION_CODE.exec(code);
    if (match && pageCodes.has(match[1])) next.add(match[1]);
    return next;
  }
  next.delete(code);
  if (pageCodes.has(code)) {
    for (const suffix of ACTION_SUFFIXES) {
      const action = `${code}:${suffix}`;
      if (!isMandatory(action)) next.delete(action);
    }
  }
  return next;
}

/** Minuscules sans accents, pour comparer une recherche à un libellé. */
export function normalizeSearch(text: string): string {
  return text.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase().trim();
}

export function matchesSearch(label: string, query: string): boolean {
  const q = normalizeSearch(query);
  return q === '' || normalizeSearch(label).includes(q);
}

/** Catégories restreintes aux droits dont le libellé correspond ; les catégories vides disparaissent. Ne modifie pas ses arguments. */
export function filterCategories<C extends { items: { label: string }[] }>(categories: readonly C[], query: string): C[] {
  if (normalizeSearch(query) === '') return [...categories];
  return categories
    .map((cat) => ({ ...cat, items: cat.items.filter((item) => matchesSearch(item.label, query)) }))
    .filter((cat) => cat.items.length > 0);
}

/** Vrai si une des étiquettes correspond (sections Bases RH / Paie et Bases de pointage). */
export function matchesAnySearch(labels: readonly string[], query: string): boolean {
  return normalizeSearch(query) === '' || labels.some((label) => matchesSearch(label, query));
}
