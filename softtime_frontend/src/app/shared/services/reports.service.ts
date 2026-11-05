import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from '../../core/api';
import {
  AuditConfig,
  AuditEvent,
  AuditEventFilter,
  AuditTypeOption,
  Canteen,
  CanteenAnomaly,
  ExtrasSyncResult,
  HistoryAnomalie,
  HistoryDeleteResult,
  HistoryHs,
  HistoryPointage,
  PagedPeriodRequest,
  PagedResult,
  PeriodRequest,
  PurgeExtrasResult,
  ReportFilter,
  ReportRow,
  Travel,
} from '../models';

@Injectable({ providedIn: 'root' })
export class ReportsService extends ApiService {
  pointage(filter: ReportFilter): Observable<ReportRow[]> {
    return this.get('pointage', filter);
  }
  absences(filter: ReportFilter): Observable<ReportRow[]> {
    return this.get('absences', filter);
  }
  retards(filter: ReportFilter): Observable<ReportRow[]> {
    return this.get('retards', filter);
  }
  hsRecap(filter: ReportFilter): Observable<ReportRow[]> {
    return this.get('hs-recap', filter);
  }
  heuresSemaine(filter: ReportFilter): Observable<ReportRow[]> {
    return this.get('heures-semaine', filter);
  }
  heuresDimanche(filter: ReportFilter): Observable<ReportRow[]> {
    return this.get('heures-dimanche', filter);
  }
  leave(matricule: string, date: string): Observable<ReportRow[]> {
    return this.http.get<ReportRow[]>(this.url('/api/reports/leave'), {
      params: this.params({ matricule, date }),
    });
  }

  private get(path: string, filter: ReportFilter): Observable<ReportRow[]> {
    return this.http.get<ReportRow[]>(this.url(`/api/reports/${path}`), {
      params: this.params({ ...filter }),
    });
  }

  /** Version paginée d'un état (6 types : pointage, absences, retards, hs-recap, heures-semaine, heures-dimanche). */
  paged(type: string, filter: ReportFilter): Observable<PagedResult<ReportRow>> {
    return this.http.get<PagedResult<ReportRow>>(this.url(`/api/reports/${type}/paged`), {
      params: this.params({ ...filter }),
    });
  }
}

@Injectable({ providedIn: 'root' })
export class CanteenService extends ApiService {
  list(req: PeriodRequest): Observable<Canteen[]> {
    return this.http.get<Canteen[]>(this.url('/api/canteen'), { params: this.params({ ...req }) });
  }
  /** Liste paginée des cantines calculées. */
  listPaged(filter: PagedPeriodRequest): Observable<PagedResult<Canteen>> {
    return this.http.get<PagedResult<Canteen>>(this.url('/api/canteen/paged'), {
      params: this.params({ ...filter }),
    });
  }
  compute(req: PeriodRequest): Observable<Canteen[]> {
    return this.http.post<Canteen[]>(this.url('/api/canteen/compute'), req);
  }
  anomalies(req: PeriodRequest): Observable<CanteenAnomaly[]> {
    return this.http.get<CanteenAnomaly[]>(this.url('/api/canteen/anomalies'), { params: this.params({ ...req }) });
  }
  /** Anomalies cantine paginées (calcul complet côté serveur puis page). */
  anomaliesPaged(filter: PagedPeriodRequest): Observable<PagedResult<CanteenAnomaly>> {
    return this.http.get<PagedResult<CanteenAnomaly>>(this.url('/api/canteen/anomalies/paged'), {
      params: this.params({ ...filter }),
    });
  }
  /** Pousse les totaux cantine de la période dans le cumul SAGE. */
  sync(req: PeriodRequest): Observable<ExtrasSyncResult> {
    return this.http.post<ExtrasSyncResult>(this.url('/api/canteen/sync'), req);
  }
  purge(req: PeriodRequest): Observable<PurgeExtrasResult> {
    return this.http.delete<PurgeExtrasResult>(this.url('/api/canteen'), { params: this.params({ ...req }) });
  }
}

@Injectable({ providedIn: 'root' })
export class TravelService extends ApiService {
  list(req: PeriodRequest): Observable<Travel[]> {
    return this.http.get<Travel[]>(this.url('/api/travel-expenses'), { params: this.params({ ...req }) });
  }
  /** Liste paginée des frais de déplacement. */
  listPaged(filter: PagedPeriodRequest): Observable<PagedResult<Travel>> {
    return this.http.get<PagedResult<Travel>>(this.url('/api/travel-expenses/paged'), {
      params: this.params({ ...filter }),
    });
  }
  compute(req: PeriodRequest): Observable<Travel[]> {
    return this.http.post<Travel[]>(this.url('/api/travel-expenses/compute'), req);
  }
  /** Pousse les totaux de frais de déplacement de la période dans le cumul SAGE. */
  sync(req: PeriodRequest): Observable<ExtrasSyncResult> {
    return this.http.post<ExtrasSyncResult>(this.url('/api/travel-expenses/sync'), req);
  }
}

@Injectable({ providedIn: 'root' })
export class AuditService extends ApiService {
  types(): Observable<AuditTypeOption[]> {
    return this.http.get<AuditTypeOption[]>(this.url('/api/audit/types'));
  }
  config(): Observable<AuditConfig[]> {
    return this.http.get<AuditConfig[]>(this.url('/api/audit/config'));
  }
  /** Configuration d'audit paginée. */
  configPaged(page: number, pageSize: number, search?: string | null): Observable<PagedResult<AuditConfig>> {
    return this.http.get<PagedResult<AuditConfig>>(this.url('/api/audit/config/paged'), {
      params: this.params({ page, pageSize, search }),
    });
  }
  createConfig(dto: AuditConfig): Observable<void> {
    return this.http.post<void>(this.url('/api/audit/config'), dto);
  }
  updateConfig(id: number, dto: AuditConfig): Observable<void> {
    return this.http.put<void>(this.url(`/api/audit/config/${id}`), dto);
  }
  events(filter: {
    from?: string;
    to?: string;
    table?: string;
    action?: string;
  }): Observable<AuditEvent[]> {
    return this.http.get<AuditEvent[]>(this.url('/api/audit/events'), {
      params: this.params({ ...filter }),
    });
  }
  /** Journal paginé (count + OFFSET/FETCH côté serveur). */
  eventsPaged(filter: AuditEventFilter): Observable<PagedResult<AuditEvent>> {
    return this.http.get<PagedResult<AuditEvent>>(this.url('/api/audit/events/paged'), {
      params: this.params({ ...filter }),
    });
  }
  schema(): Observable<ReportRow[]> {
    return this.http.get<ReportRow[]>(this.url('/api/audit/schema'));
  }
}

/** Archives des suppressions : consultation et purge. Le filtre porte sur la date de suppression. */
@Injectable({ providedIn: 'root' })
export class HistoryService extends ApiService {
  pointages(filter: ReportFilter): Observable<HistoryPointage[]> {
    return this.list('pointages', filter);
  }
  anomalies(filter: ReportFilter): Observable<HistoryAnomalie[]> {
    return this.list('anomalies', filter);
  }
  hs(filter: ReportFilter): Observable<HistoryHs[]> {
    return this.list('hs', filter);
  }
  /** Archives paginées, par segment. */
  pointagesPaged(filter: ReportFilter): Observable<PagedResult<HistoryPointage>> {
    return this.listPaged('pointages', filter);
  }
  anomaliesPaged(filter: ReportFilter): Observable<PagedResult<HistoryAnomalie>> {
    return this.listPaged('anomalies', filter);
  }
  hsPaged(filter: ReportFilter): Observable<PagedResult<HistoryHs>> {
    return this.listPaged('hs', filter);
  }
  removeOne(segment: string, id: number): Observable<void> {
    return this.http.delete<void>(this.url(`/api/history/${segment}/${id}`));
  }
  purgeAll(segment: string): Observable<HistoryDeleteResult> {
    return this.http.delete<HistoryDeleteResult>(this.url(`/api/history/${segment}`));
  }

  private list<T>(segment: string, filter: ReportFilter): Observable<T[]> {
    return this.http.get<T[]>(this.url(`/api/history/${segment}`), {
      params: this.params({ ...filter }),
    });
  }

  private listPaged<T>(segment: string, filter: ReportFilter): Observable<PagedResult<T>> {
    return this.http.get<PagedResult<T>>(this.url(`/api/history/${segment}/paged`), {
      params: this.params({ ...filter }),
    });
  }
}
