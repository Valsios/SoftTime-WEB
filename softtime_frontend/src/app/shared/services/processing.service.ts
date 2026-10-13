import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from '../../core/api';
import {
  Anomaly,
  AnomalyUpdate,
  AnomalyValidationResult,
  CorrectedHour,
  EmployeePlanning,
  HsExo,
  ImportPunchesRequest,
  ImportResult,
  OvertimeDetail,
  PagedPeriodRequest,
  PagedResult,
  PeriodRequest,
  PlanningEmployee,
  PreprocessResult,
  Punch,
  PunchUpdate,
  PurgePunchesResult,
  PurgeValidationResult,
  Shift,
  WeeklyHs,
  WeeklyValidationRequest,
  PayrollWriteResult,
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
  /** CSV « catégorie ; lundi ; (début ; fin) × 7 » : génère la semaine pour toute la catégorie. */
  generateFromCategory(file: File): Observable<ImportResult> {
    const form = new FormData();
    form.append('file', file);
    return this.http.post<ImportResult>(this.url('/api/employee-planning/generate-from-category'), form);
  }
  /** CSV « matricule ; lundi ; (début ; pause ; fin) × 7 » : génère la semaine pour chaque salarié. */
  generate(file: File): Observable<ImportResult> {
    const form = new FormData();
    form.append('file', file);
    return this.http.post<ImportResult>(this.url('/api/employee-planning/generate'), form);
  }
}

@Injectable({ providedIn: 'root' })
export class PunchesService extends ApiService {
  list(filter: PeriodRequest): Observable<Punch[]> {
    return this.http.get<Punch[]>(this.url('/api/punches'), {
      params: this.params({ ...filter }),
    });
  }
  /**
   * Même liste, paginée. Le contrat de la liste complète est conservé pour l'export Excel
   * (qui porte sur la grille entière).
   */
  listPaged(filter: PagedPeriodRequest): Observable<PagedResult<Punch>> {
    return this.http.get<PagedResult<Punch>>(this.url('/api/punches/paged'), {
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
  /** Pré-traitement : pointages → heures corrigées (T_HEURECORRIGER) + anomalies. */
  preprocess(req: PeriodRequest): Observable<PreprocessResult> {
    return this.http.post<PreprocessResult>(this.url('/api/punches/preprocess'), req);
  }
  /** Corrige un pointage importé (refusé si la semaine est validée). */
  update(id: number, dto: PunchUpdate): Observable<Punch> {
    return this.http.put<Punch>(this.url(`/api/punches/${id}`), dto);
  }
  /** Purge la période : pointages + corrections automatiques + anomalies (archivés). */
  purge(filter: PeriodRequest): Observable<PurgePunchesResult> {
    return this.http.delete<PurgePunchesResult>(this.url('/api/punches'), {
      params: this.params({ ...filter }),
    });
  }
}

@Injectable({ providedIn: 'root' })
export class AnomaliesService extends ApiService {
  list(filter: PeriodRequest): Observable<Anomaly[]> {
    return this.http.get<Anomaly[]>(this.url('/api/anomalies'), {
      params: this.params({ ...filter }),
    });
  }
  get(id: number): Observable<Anomaly> {
    return this.http.get<Anomaly>(this.url(`/api/anomalies/${id}`));
  }
  /** Arbitre une anomalie : heures retenues + drapeaux d'absence / férié. */
  update(id: number, dto: AnomalyUpdate): Observable<Anomaly> {
    return this.http.put<Anomaly>(this.url(`/api/anomalies/${id}`), dto);
  }
  /** Convertit les anomalies sélectionnées en heures corrigées puis les supprime. */
  validate(ids: number[]): Observable<AnomalyValidationResult> {
    return this.http.post<AnomalyValidationResult>(this.url('/api/anomalies/validate'), { ids });
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
  /** Même liste, paginée (voir `PunchesService.listPaged`). */
  listPaged(filter: PagedPeriodRequest): Observable<PagedResult<CorrectedHour>> {
    return this.http.get<PagedResult<CorrectedHour>>(this.url('/api/corrected-hours/paged'), {
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
  /** Purge la validation de la semaine (supprime T_HS_VALIDER et déverrouille les heures). */
  purge(filter: PeriodRequest): Observable<PurgeValidationResult> {
    return this.http.delete<PurgeValidationResult>(this.url('/api/weekly-validation'), {
      params: this.params({ ...filter }),
    });
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
  syncPayroll(req: PeriodRequest): Observable<PayrollWriteResult> {
    return this.http.post<PayrollWriteResult>(this.url('/api/overtime/sync-payroll'), req);
  }
  /** Détail du calcul HS d'un salarié : jours, semaines EXO/IMPO et allocation enregistrée. */
  detail(matricule: string, from: string, to: string): Observable<OvertimeDetail> {
    return this.http.get<OvertimeDetail>(this.url(`/api/overtime/detail/${encodeURIComponent(matricule)}`), {
      params: this.params({ from, to }),
    });
  }
}
