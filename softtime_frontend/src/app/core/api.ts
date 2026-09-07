import { HttpClient, HttpParams } from '@angular/common/http';
import { inject } from '@angular/core';
import { environment } from '../../environments/environment';

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
}
