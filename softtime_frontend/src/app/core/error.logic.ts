export const FORBIDDEN_DEFAULT_MESSAGE =
  "Accès refusé : votre compte n'a pas le droit requis pour cette fonction.";
export const BASE_ACCESS_DENIED_MESSAGE = "Vous n'avez pas accès à cette base.";
const BASE_ACCESS_DENIED_VARIANTS = [BASE_ACCESS_DENIED_MESSAGE];

export interface HttpErrorLike {
  status: number;
  error?: unknown;
  message?: string | null;
}

/** Message applicatif du corps de la réponse (`{ error }`, `{ message }`, `{ title }` ou texte), ou chaîne vide. */
export function bodyMessage(err: HttpErrorLike): string {
  const body = err.error;
  if (typeof body === 'string') return body.trim();
  if (body && typeof body === 'object') {
    const b = body as { error?: unknown; message?: unknown; title?: unknown };
    for (const v of [b.error, b.message, b.title]) {
      if (typeof v === 'string' && v.trim()) return v.trim();
    }
  }
  return '';
}

/** 400 « En-tête X-Sage-Database requis. » : aucune base active, remplacé par l'état vide. */
export function isMissingBaseError(err: HttpErrorLike): boolean {
  return err.status === 400 && bodyMessage(err).includes('X-Sage-Database');
}

/** 403 applicatif « Vous n'avez pas accès à cette base. » : l'accès à la base a changé en cours de session. */
export function isBaseAccessDenied(err: HttpErrorLike): boolean {
  return err.status === 403 && BASE_ACCESS_DENIED_VARIANTS.includes(bodyMessage(err));
}

/** Texte affiché pour une erreur. Un 403 sans corps ne laisse pas le texte natif d'Angular. */
export function errorMessage(err: HttpErrorLike): string {
  const message = bodyMessage(err);
  if (message) return message;
  if (err.status === 403) return FORBIDDEN_DEFAULT_MESSAGE;
  return err.message || 'Une erreur est survenue.';
}
