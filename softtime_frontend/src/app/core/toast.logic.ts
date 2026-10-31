/** Fenêtre de déduplication des messages d'erreur identiques. */
export const TOAST_DEDUPE_WINDOW_MS = 3000;

/**
 * Vrai si le message doit être affiché. Un même message d'erreur déjà affiché dans la fenêtre est ignoré.
 * Seules les erreurs sont dédupliquées ; `lastShown` mémorise l'instant du dernier affichage par message.
 */
export function shouldShowToast(
  lastShown: Map<string, number>,
  kind: string,
  message: string,
  now: number,
  windowMs: number = TOAST_DEDUPE_WINDOW_MS,
): boolean {
  if (kind !== 'error') return true;
  for (const [key, at] of lastShown) {
    if (now - at >= windowMs) lastShown.delete(key);
  }
  const previous = lastShown.get(message);
  if (previous !== undefined && now - previous < windowMs) return false;
  lastShown.set(message, now);
  return true;
}
