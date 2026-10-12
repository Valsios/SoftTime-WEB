import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from '../../core/api';
import { DashboardFilters, DashboardStats, PeriodRequest } from '../models';

export interface DashboardQuery extends PeriodRequest {
  periode?: string;
  departement?: string | null;
  categorie?: number | null;
}

/** Tableau de bord analytique : KPI, absences, retards, répartition et assiduité. */
@Injectable({ providedIn: 'root' })
export class DashboardService extends ApiService {
  stats(req: DashboardQuery): Observable<DashboardStats> {
    return this.http.get<DashboardStats>(this.url('/api/dashboard/stats'), {
      params: this.params({ ...req }),
    });
  }

  /** Valeurs des filtres (départements, branches, catégories de personnel). */
  filters(): Observable<DashboardFilters> {
    return this.http.get<DashboardFilters>(this.url('/api/dashboard/filters'));
  }
}
