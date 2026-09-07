import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from '../../core/api';
import {
  AuditConfig,
  AuditEvent,
  Canteen,
  PeriodRequest,
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
}

@Injectable({ providedIn: 'root' })
export class CanteenService extends ApiService {
  list(req: PeriodRequest): Observable<Canteen[]> {
    return this.http.get<Canteen[]>(this.url('/api/canteen'), { params: this.params({ ...req }) });
  }
  compute(req: PeriodRequest): Observable<Canteen[]> {
    return this.http.post<Canteen[]>(this.url('/api/canteen/compute'), req);
  }
}

@Injectable({ providedIn: 'root' })
export class TravelService extends ApiService {
  list(req: PeriodRequest): Observable<Travel[]> {
    return this.http.get<Travel[]>(this.url('/api/travel-expenses'), { params: this.params({ ...req }) });
  }
  compute(req: PeriodRequest): Observable<Travel[]> {
    return this.http.post<Travel[]>(this.url('/api/travel-expenses/compute'), req);
  }
}

@Injectable({ providedIn: 'root' })
export class AuditService extends ApiService {
  config(): Observable<AuditConfig[]> {
    return this.http.get<AuditConfig[]>(this.url('/api/audit/config'));
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
  schema(): Observable<ReportRow[]> {
    return this.http.get<ReportRow[]>(this.url('/api/audit/schema'));
  }
}
