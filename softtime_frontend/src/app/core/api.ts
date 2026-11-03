import { HttpClient, HttpParams } from '@angular/common/http';
import { inject } from '@angular/core';
import { Observable, from, map, mergeMap, of, switchMap, toArray } from 'rxjs';
import { environment } from '../../environments/environment';
import { PagedResult } from '../shared/models';
import { ALL_PAGES_CONCURRENCY, ALL_PAGES_SIZE, mergePages, remainingPages } from './paging.logic';

/**
 * Base class for domain HTTP services. Each functional service extends this and
 * supplies its resource path; the base wires the shared HttpClient + base URL.
 */
export abstract class ApiService {
  protected readonly http = inject(HttpClient);
  protected readonly base = environment.apiBaseUrl;

  protected url(path: string): string {
    return `${this.base}${path}`;
  }

  protected params(obj: Record<string, unknown>): HttpParams {
    let p = new HttpParams();
    for (const [key, value] of Object.entries(obj)) {
      if (value !== undefined && value !== null && value !== '') {
        p = p.set(key, String(value));
      }
    }
    return p;
  }

  /**
   * Toutes les lignes d'une liste paginée (options de sélection, exports) : première page, puis les pages
   * restantes en parallèle limité. Le serveur ne renvoie jamais plus de {@link ALL_PAGES_SIZE} lignes par requête.
   */
  protected allPages<T>(path: string, filters: Record<string, unknown> = {}): Observable<T[]> {
    const page = (n: number) =>
      this.http
        .get<PagedResult<T>>(this.url(path), {
          params: this.params({ ...filters, page: n, pageSize: ALL_PAGES_SIZE }),
        })
        .pipe(map((res) => ({ page: n, items: res.items ?? [], totalPages: res.totalPages })));

    return page(1).pipe(
      switchMap((first) => {
        const rest = remainingPages(first.totalPages);
        if (rest.length === 0) return of(first.items);
        return from(rest).pipe(
          mergeMap((n) => page(n), ALL_PAGES_CONCURRENCY),
          toArray(),
          map((pages) => mergePages([first, ...pages])),
        );
      }),
    );
  }
}
