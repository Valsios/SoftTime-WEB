import { HttpInterceptorFn, HttpResponse } from '@angular/common/http';
import { map } from 'rxjs';
import { isApiEnvelope } from '../api-envelope';

/**
 * Déballe l'enveloppe `{ success, message, data }` des réponses de succès : les services reçoivent directement
 * les données, comme avant la normalisation du contrat d'API. Les fichiers (blob) ne sont pas concernés.
 */
export const envelopeInterceptor: HttpInterceptorFn = (req, next) =>
  next(req).pipe(
    map((event) =>
      event instanceof HttpResponse && isApiEnvelope(event.body)
        ? event.clone({ body: event.body.data ?? null })
        : event,
    ),
  );
