import type { HttpErrorResponse } from '@angular/common/http';

/** Corps d'erreur renvoyé par l'API : messages applicatifs ou `ProblemDetails` MVC. */
export interface ApiErrorBody {
  error?: string;
  message?: string;
  title?: string;
  /** Champ concerné, renseigné par le middleware de validation métier. */
  field?: string;
  /** Erreurs de validation MVC, indexées par champ. */
  errors?: Record<string, string | string[] | null | undefined>;
}

/**
 * Erreurs par champ extraites d'une réponse API.
 * `trusted` distingue les messages métier (français) des messages MVC (`ProblemDetails`),
 * que l'interface remplace par ses propres libellés.
 */
export interface ApiFieldErrors {
  fields: Record<string, string>;
  trusted: Record<string, true>;
}

const FALLBACK_MESSAGE = 'Une erreur est survenue.';

export function apiErrorBody(err: HttpErrorResponse): ApiErrorBody | null {
  const body: unknown = err.error;
  return body !== null && typeof body === 'object' ? (body as ApiErrorBody) : null;
}

/** Message lisible : message métier, puis erreurs de validation, puis `ProblemDetails`. */
export function apiErrorMessage(err: HttpErrorResponse): string {
  const body = apiErrorBody(err);
  if (body) {
    if (body.error) return body.error;
    const details = fieldMessages(body.errors);
    if (details.length) return details.join(' ');
    if (body.message) return body.message;
    if (body.title) return body.title;
  }
  if (typeof err.error === 'string' && err.error.trim()) return err.error;
  return err.message || FALLBACK_MESSAGE;
}

/** Erreurs par champ, avec des clés normalisées (`$.nom`, `Nom`, `nom` → `nom`). */
export function apiFieldErrors(err: HttpErrorResponse): ApiFieldErrors {
  const body = apiErrorBody(err);
  const fields: Record<string, string> = {};
  const trusted: Record<string, true> = {};
  if (!body) return { fields, trusted };

  if (body.field && body.error) {
    const key = normalizeFieldKey(body.field);
    fields[key] = body.error;
    trusted[key] = true;
  }

  for (const [rawKey, value] of Object.entries(body.errors ?? {})) {
    const key = normalizeFieldKey(rawKey);
    const message = Array.isArray(value)
      ? value.find((entry) => typeof entry === 'string' && entry.trim().length > 0)
      : value;
    if (!message) continue;
    fields[key] ??= message;
  }

  return { fields, trusted };
}

/** Normalise une clé de champ : dernière partie, en minuscules (`$.roleId` → `roleid`). */
export function normalizeFieldKey(key: string): string {
  const clean = key.replace(/^\$\.?/, '').replace(/[[\]]/g, '.');
  const parts = clean.split('.').filter(Boolean);
  return (parts.length ? parts[parts.length - 1] : clean).toLowerCase();
}

function fieldMessages(errors: ApiErrorBody['errors']): string[] {
  if (!errors) return [];
  return Object.values(errors)
    .flatMap((value) => (Array.isArray(value) ? value : [value]))
    .filter((value): value is string => typeof value === 'string' && value.trim().length > 0);
}
