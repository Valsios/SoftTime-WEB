import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from '../../core/api';
import { JournalEntry, JournalFacets, JournalPage, JournalQueryParams } from '../models';

/** Lecture seule du journal central : aucune route d'écriture n'existe côté serveur. */
@Injectable({ providedIn: 'root' })
export class JournalService extends ApiService {
  list(query: JournalQueryParams): Observable<JournalPage> {
    return this.http.get<JournalPage>(this.url('/api/journal'), { params: this.params({ ...query }) });
  }

  get(id: number): Observable<JournalEntry> {
    return this.http.get<JournalEntry>(this.url(`/api/journal/${id}`));
  }

  facets(from?: string, to?: string): Observable<JournalFacets> {
    return this.http.get<JournalFacets>(this.url('/api/journal/facets'), { params: this.params({ from, to }) });
  }
}
