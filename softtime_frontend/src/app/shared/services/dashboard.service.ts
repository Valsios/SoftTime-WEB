import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from '../../core/api';
import { DashboardStats, PeriodRequest } from '../models';

/** Tableau de bord : KPI, absences et retards de la période. */
@Injectable({ providedIn: 'root' })
export class DashboardService extends ApiService {
  stats(req: PeriodRequest & { periode?: string; departement?: string | null }): Observable<DashboardStats> {
    return this.http.get<DashboardStats>(this.url('/api/dashboard/stats'), {
      params: this.params({ ...req }),
    });
  }
}
