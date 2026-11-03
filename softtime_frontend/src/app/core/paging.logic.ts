/**
 * Chargement complet d'une liste paginée côté serveur (convention : tout GET de liste est paginé).
 * Le serveur plafonne la taille de page à 100 : une liste complète est lue en plusieurs requêtes.
 */
export const ALL_PAGES_SIZE = 100;

/** Requêtes simultanées au plus pour les pages suivant la première. */
export const ALL_PAGES_CONCURRENCY = 4;

/** Numéros des pages restant à lire après la première (vide pour une liste d'une page ou moins). */
export function remainingPages(totalPages: number): number[] {
  const last = Number.isFinite(totalPages) ? Math.floor(totalPages) : 1;
  return last <= 1 ? [] : Array.from({ length: last - 1 }, (_, i) => i + 2);
}

/** Lignes des pages remises dans l'ordre des pages, quel que soit l'ordre d'arrivée des réponses. */
export function mergePages<T>(pages: ReadonlyArray<{ page: number; items: T[] }>): T[] {
  return [...pages].sort((a, b) => a.page - b.page).flatMap((p) => p.items);
}
