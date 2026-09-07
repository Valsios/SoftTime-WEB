import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from '../../core/api';
import {
  Anomaly,
  CorrectedHour,
  EmployeePlanning,
  HsExo,
  ImportPunchesRequest,
  ImportResult,
  PeriodRequest,
  PlanningEmployee,
  Punch,
  Shift,
  WeeklyHs,
  WeeklyValidationRequest,
} from '../models';

@Injectable({ providedIn: 'root' })
export class ShiftsService extends ApiService {
  list(): Observable<Shift[]> {
    return this.http.get<Shift[]>(this.url('/api/shifts'));
  }
  create(dto: Shift): Observable<Shift> {
    return this.http.post<Shift>(this.url('/api/shifts'), dto);
  }
  update(id: number, dto: Shift): Observable<Shift> {
    return this.http.put<Shift>(this.url(`/api/shifts/${id}`), dto);
  }
  remove(id: number): Observable<void> {
    return this.http.delete<void>(this.url(`/api/shifts/${id}`));
  }
  importExcel(file: File): Observable<ImportResult> {
    const form = new FormData();
    form.append('file', file);
    return this.http.post<ImportResult>(this.url('/api/shifts/import-excel'), form);
  }
}

@Injectable({ providedIn: 'root' })
export class EmployeePlanningService extends ApiService {
  list(from: string, to: string, cardId?: number): Observable<EmployeePlanning[]> {
    return this.http.get<EmployeePlanning[]>(this.url('/api/employee-planning'), {
      params: this.params({ from, to, cardId }),
    });
  }
  employees(): Observable<PlanningEmployee[]> {
    return this.http.get<PlanningEmployee[]>(this.url('/api/employee-planning/employees'));
  }
  save(rows: EmployeePlanning[]): Observable<void> {
    return this.http.post<void>(this.url('/api/employee-planning'), rows);
  }
  update(rows: EmployeePlanning[]): Observable<void> {
    return this.http.put<void>(this.url('/api/employee-planning'), rows);
  }
  remove(id: number): Observable<void> {
    return this.http.delete<void>(this.url(`/api/employee-planning/${id}`));
  }
  importExcel(file: File): Observable<ImportResult> {
    const form = new FormData();
    form.append('file', file);
    return this.http.post<ImportResult>(this.url('/api/employee-planning/import-excel'), form);
  }
}

@Injectable({ providedIn: 'root' })
export class PunchesService extends ApiService {
  list(filter: PeriodRequest): Observable<Punch[]> {
    return this.http.get<Punch[]>(this.url('/api/punches'), {
      params: this.params({ ...filter }),
    });
  }
  importClock(body: ImportPunchesRequest): Observable<ImportResult> {
    return this.http.post<ImportResult>(this.url('/api/punches/import-clock'), body);
  }
  importExcel(file: File): Observable<ImportResult> {
    const form = new FormData();
    form.append('file', file);
    return this.http.post<ImportResult>(this.url('/api/punches/import-excel'), form);
  }
}

@Injectable({ providedIn: 'root' })
export class AnomaliesService extends ApiService {
  list(filter: PeriodRequest): Observable<Anomaly[]> {
    return this.http.get<Anomaly[]>(this.url('/api/anomalies'), {
      params: this.params({ ...filter }),
    });
  }
  remove(id: number): Observable<void> {
    return this.http.delete<void>(this.url(`/api/anomalies/${id}`));
  }
}

@Injectable({ providedIn: 'root' })
export class CorrectedHoursService extends ApiService {
  list(filter: PeriodRequest): Observable<CorrectedHour[]> {
    return this.http.get<CorrectedHour[]>(this.url('/api/corrected-hours'), {
      params: this.params({ ...filter }),
    });
  }
  create(dto: CorrectedHour): Observable<CorrectedHour> {
    return this.http.post<CorrectedHour>(this.url('/api/corrected-hours'), dto);
  }
  update(id: number, dto: CorrectedHour): Observable<CorrectedHour> {
    return this.http.put<CorrectedHour>(this.url(`/api/corrected-hours/${id}`), dto);
  }
  remove(id: number): Observable<void> {
    return this.http.delete<void>(this.url(`/api/corrected-hours/${id}`));
  }
  importExcel(file: File): Observable<ImportResult> {
    const form = new FormData();
    form.append('file', file);
    return this.http.post<ImportResult>(this.url('/api/corrected-hours/import-excel'), form);
  }
}

@Injectable({ providedIn: 'root' })
export class WeeklyValidationService extends ApiService {
  preview(req: PeriodRequest): Observable<WeeklyHs[]> {
    return this.http.post<WeeklyHs[]>(this.url('/api/weekly-validation/preview'), req);
  }
  validate(req: WeeklyValidationRequest): Observable<void> {
    return this.http.post<void>(this.url('/api/weekly-validation/validate'), req);
  }
  unvalidate(req: WeeklyValidationRequest): Observable<void> {
    return this.http.post<void>(this.url('/api/weekly-validation/unvalidate'), req);
  }
}

@Injectable({ providedIn: 'root' })
export class OvertimeService extends ApiService {
  list(req: PeriodRequest): Observable<HsExo[]> {
    return this.http.get<HsExo[]>(this.url('/api/overtime'), {
      params: this.params({ ...req }),
    });
  }
  calculate(req: PeriodRequest): Observable<HsExo[]> {
    return this.http.post<HsExo[]>(this.url('/api/overtime/calculate'), req);
  }
  purge(req: PeriodRequest): Observable<void> {
    return this.http.post<void>(this.url('/api/overtime/purge'), req);
  }
  syncSage(req: PeriodRequest): Observable<void> {
    return this.http.post<void>(this.url('/api/overtime/sync-sage'), req);
  }
}
