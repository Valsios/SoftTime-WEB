/** Normalise un texte pour une recherche insensible à la casse et aux accents. */
export function normalizeSearchText(value: unknown): string {
  if (value === null || value === undefined) return '';
  return String(value)
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '')
    .toLowerCase()
    .replace(/\s+/g, ' ')
    .trim();
}

/** Découpe un terme de recherche en mots distincts. */
export function searchTerms(value: string | null | undefined): string[] {
  const normalized = normalizeSearchText(value);
  return normalized ? [...new Set(normalized.split(' ').filter(Boolean))] : [];
}

/**
 * Vrai si tous les mots du terme sont présents dans au moins une des valeurs.
 * Un terme vide ne filtre rien.
 */
export function matchesSearch(
  search: string | null | undefined,
  values: readonly unknown[],
): boolean {
  const terms = searchTerms(search);
  if (terms.length === 0) return true;
  const haystack = values
    .filter((value) => value !== null && value !== undefined)
    .map((value) => normalizeSearchText(value))
    .join(' ');
  return terms.every((term) => haystack.includes(term));
}
