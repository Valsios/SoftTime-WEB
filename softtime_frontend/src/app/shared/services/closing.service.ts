import { HttpBackend, HttpClient, HttpHeaders } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from '../../core/api';
import { SessionStore } from '../../core/session.store';
import { ClosingChecklist, ClosingQuery, ClosingReport } from '../models';

/**
 * Clôture de paie assistée : un seul appel en LECTURE (GET api/closing/analysis), aucune écriture.
 * Les jours sont envoyés en UTC avec « Z » (minuit UTC du jour choisi) : le serveur garde ainsi
 * le jour demandé quel que soit le fuseau du navigateur.
 */
@Injectable({ providedIn: 'root' })
export class ClosingService extends ApiService {
  private readonly session = inject(SessionStore);
  /** Client sans intercepteurs : réservé à la carte du tableau de bord, qui doit échouer en silence. */
  private readonly silentHttp = new HttpClient(inject(HttpBackend));

  analysis(query: ClosingQuery): Observable<ClosingReport> {
    return this.http.get<ClosingReport>(this.url('/api/closing/analysis'), { params: this.params(toParams(query)) });
  }

  /** Checklist d'avancement de la paie (GET api/closing/checklist) : même période et même département que l'analyse. */
  checklist(query: Pick<ClosingQuery, 'from' | 'to' | 'departement'>): Observable<ClosingChecklist> {
    return this.http.get<ClosingChecklist>(this.url('/api/closing/checklist'), {
      params: this.params({ from: dayToUtcIso(query.from), to: dayToUtcIso(query.to), departement: query.departement }),
    });
  }

  /**
   * Même lecture, sans toast ni redirection en cas d'erreur (carte du tableau de bord). Les en-têtes
   * sont ceux que pose authInterceptor (jeton, bases actives) : aucun droit supplémentaire.
   */
  analysisSilently(query: ClosingQuery): Observable<ClosingReport> {
    let headers = new HttpHeaders();
    const token = this.session.token();
    const sage = this.session.activeSageDb();
    const pointeuse = this.session.activePointeuseDb();
    if (token) headers = headers.set('Authorization', `Bearer ${token}`);
    if (sage) headers = headers.set('X-Sage-Database', sage);
    if (pointeuse) headers = headers.set('X-Pointeuse-Database', pointeuse);
    return this.silentHttp.get<ClosingReport>(this.url('/api/closing/analysis'), {
      headers,
      params: this.params(toParams(query)),
    });
  }
}

/** Jour AAAA-MM-JJ → minuit UTC « AAAA-MM-JJT00:00:00Z » (rien si vide ou illisible). */
export function dayToUtcIso(day: string | null | undefined): string | undefined {
  return day && /^\d{4}-\d{2}-\d{2}$/.test(day) ? `${day}T00:00:00Z` : undefined;
}

function toParams(query: ClosingQuery): Record<string, unknown> {
  return {
    from: dayToUtcIso(query.from),
    to: dayToUtcIso(query.to),
    departement: query.departement,
    includeComparison: query.includeComparison ? true : undefined,
    includeInformation: query.includeInformation ? true : undefined,
    page: query.page,
    pageSize: query.pageSize,
    reuseCache: query.reuseCache ? true : undefined,
    gravites: query.gravites?.length ? query.gravites.join(',') : undefined,
    tri: query.tri === 'gravite' ? 'gravite' : undefined,
  };
}
