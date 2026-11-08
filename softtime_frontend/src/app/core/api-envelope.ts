/**
 * Enveloppe standard des réponses de l'API : `{ success, message, data, timestamp }`, avec `field` et
 * `errors` en cas d'échec de validation.
 */
export interface ApiEnvelope<T = unknown> {
  success: boolean;
  message: string;
  data?: T;
  field?: string;
  errors?: Record<string, string[]>;
  timestamp: string;
}

/** Vrai pour un corps au format enveloppe de l'API. */
export function isApiEnvelope(body: unknown): body is ApiEnvelope {
  return (
    body !== null &&
    typeof body === 'object' &&
    typeof (body as { success?: unknown }).success === 'boolean' &&
    'timestamp' in body
  );
}

/** Données utiles d'une réponse : le contenu de `data` pour une enveloppe, le corps tel quel sinon. */
export function unwrapApiBody(body: unknown): unknown {
  return isApiEnvelope(body) ? (body.data ?? null) : body;
}
