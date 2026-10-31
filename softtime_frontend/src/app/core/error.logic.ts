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
    const b = body as { error?: unknown; errors?: unknown; message?: unknown; title?: unknown };
    // Erreurs de validation MVC (ProblemDetails) : plus parlantes que le titre générique.
    if (typeof b.error !== 'string' || !b.error.trim()) {
      const details = validationMessages(b.errors);
      if (details.length) return details.join(' ');
    }
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

/** Aperçu d'un changement de rôle renvoyé par le serveur (POST users/{id}/role-change-preview, ou corps d'un 409). */
export interface RoleChangePreview {
  adjustmentsToClear: string[];
  count: number;
  accountLogin: string;
}

export function parseRoleChangePreview(body: unknown): RoleChangePreview | null {
  if (!body || typeof body !== 'object') return null;
  const b = body as { adjustmentsToClear?: unknown; count?: unknown; accountLogin?: unknown };
  if (!Array.isArray(b.adjustmentsToClear) || typeof b.count !== 'number') return null;
  return {
    adjustmentsToClear: b.adjustmentsToClear.filter((c): c is string => typeof c === 'string'),
    count: b.count,
    accountLogin: typeof b.accountLogin === 'string' ? b.accountLogin : '',
  };
}

/** 409 « confirmation requise » : une demande de confirmation de la page, pas une erreur à afficher. */
export function previewFromConflict(err: HttpErrorLike): RoleChangePreview | null {
  return err.status === 409 ? parseRoleChangePreview(err.error) : null;
}

function validationMessages(errors: unknown): string[] {
  if (!errors || typeof errors !== 'object') return [];
  return Object.values(errors as Record<string, unknown>)
    .flatMap((value) => (Array.isArray(value) ? value : [value]))
    .filter((value): value is string => typeof value === 'string' && value.trim().length > 0)
    .map((value) => value.trim());
}
