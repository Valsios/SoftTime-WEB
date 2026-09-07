import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from '../../core/api';
import {
  AbsenceCode,
  Affectation,
  CardPaie,
  Category,
  ClockParam,
  CodeConstante,
  CorrespondenceMode,
  Holiday,
  ImportResult,
  Majoration,
  PointeuseDb,
  SageConstantOption,
  Tolerance,
} from '../models';

@Injectable({ providedIn: 'root' })
export class PointeuseDatabasesService extends ApiService {
  list(): Observable<PointeuseDb[]> {
    return this.http.get<PointeuseDb[]>(this.url('/api/pointeuse-databases'));
  }
  create(dto: PointeuseDb): Observable<PointeuseDb> {
    return this.http.post<PointeuseDb>(this.url('/api/pointeuse-databases'), dto);
  }
  update(id: number, dto: PointeuseDb): Observable<PointeuseDb> {
    return this.http.put<PointeuseDb>(this.url(`/api/pointeuse-databases/${id}`), dto);
  }
  remove(id: number): Observable<void> {
    return this.http.delete<void>(this.url(`/api/pointeuse-databases/${id}`));
  }
}

@Injectable({ providedIn: 'root' })
export class ClockParamsService extends ApiService {
  get(): Observable<ClockParam> {
    return this.http.get<ClockParam>(this.url('/api/clock-params'));
  }
  update(dto: ClockParam): Observable<ClockParam> {
    return this.http.put<ClockParam>(this.url('/api/clock-params'), dto);
  }
}

@Injectable({ providedIn: 'root' })
export class CorrespondenceModeService extends ApiService {
  get(): Observable<CorrespondenceMode> {
    return this.http.get<CorrespondenceMode>(this.url('/api/correspondence-mode'));
  }
  update(dto: CorrespondenceMode): Observable<CorrespondenceMode> {
    return this.http.put<CorrespondenceMode>(this.url('/api/correspondence-mode'), dto);
  }
}

@Injectable({ providedIn: 'root' })
export class CardPaieService extends ApiService {
  list(): Observable<CardPaie[]> {
    return this.http.get<CardPaie[]>(this.url('/api/cardpaie'));
  }
  create(dto: CardPaie): Observable<CardPaie> {
    return this.http.post<CardPaie>(this.url('/api/cardpaie'), dto);
  }
  update(id: number, dto: CardPaie): Observable<CardPaie> {
    return this.http.put<CardPaie>(this.url(`/api/cardpaie/${id}`), dto);
  }
  remove(id: number): Observable<void> {
    return this.http.delete<void>(this.url(`/api/cardpaie/${id}`));
  }
  autoMap(): Observable<{ added: number }> {
    return this.http.post<{ added: number }>(this.url('/api/cardpaie/auto-map'), {});
  }
  importCsv(file: File): Observable<ImportResult> {
    const form = new FormData();
    form.append('file', file);
    return this.http.post<ImportResult>(this.url('/api/cardpaie/import-csv'), form);
  }
}

@Injectable({ providedIn: 'root' })
export class CategoriesService extends ApiService {
  list(): Observable<Category[]> {
    return this.http.get<Category[]>(this.url('/api/categories'));
  }
  create(dto: Category): Observable<Category> {
    return this.http.post<Category>(this.url('/api/categories'), dto);
  }
  update(id: number, dto: Category): Observable<Category> {
    return this.http.put<Category>(this.url(`/api/categories/${id}`), dto);
  }
  remove(id: number): Observable<void> {
    return this.http.delete<void>(this.url(`/api/categories/${id}`));
  }
}

@Injectable({ providedIn: 'root' })
export class AffectationsService extends ApiService {
  list(categoryId?: number): Observable<Affectation[]> {
    return this.http.get<Affectation[]>(this.url('/api/affectations'), {
      params: this.params({ categoryId }),
    });
  }
  create(dto: Affectation): Observable<Affectation> {
    return this.http.post<Affectation>(this.url('/api/affectations'), dto);
  }
  update(id: number, dto: Affectation): Observable<Affectation> {
    return this.http.put<Affectation>(this.url(`/api/affectations/${id}`), dto);
  }
  remove(id: number): Observable<void> {
    return this.http.delete<void>(this.url(`/api/affectations/${id}`));
  }
  importExcel(file: File): Observable<ImportResult> {
    const form = new FormData();
    form.append('file', file);
    return this.http.post<ImportResult>(this.url('/api/affectations/import-excel'), form);
  }
}

@Injectable({ providedIn: 'root' })
export class TolerancesService extends ApiService {
  list(categoryId?: number): Observable<Tolerance[]> {
    return this.http.get<Tolerance[]>(this.url('/api/tolerances'), {
      params: this.params({ categoryId }),
    });
  }
  create(dto: Tolerance): Observable<Tolerance> {
    return this.http.post<Tolerance>(this.url('/api/tolerances'), dto);
  }
  update(id: number, dto: Tolerance): Observable<Tolerance> {
    return this.http.put<Tolerance>(this.url(`/api/tolerances/${id}`), dto);
  }
}

@Injectable({ providedIn: 'root' })
export class HolidaysService extends ApiService {
  list(): Observable<Holiday[]> {
    return this.http.get<Holiday[]>(this.url('/api/holidays'));
  }
  create(dto: Holiday): Observable<Holiday> {
    return this.http.post<Holiday>(this.url('/api/holidays'), dto);
  }
  update(id: number, dto: Holiday): Observable<Holiday> {
    return this.http.put<Holiday>(this.url(`/api/holidays/${id}`), dto);
  }
  remove(id: number): Observable<void> {
    return this.http.delete<void>(this.url(`/api/holidays/${id}`));
  }
  importSage(): Observable<void> {
    return this.http.post<void>(this.url('/api/holidays/import-sage'), {});
  }
}

@Injectable({ providedIn: 'root' })
export class MajorationsService extends ApiService {
  list(): Observable<Majoration[]> {
    return this.http.get<Majoration[]>(this.url('/api/majorations'));
  }
  update(id: number, dto: Majoration): Observable<Majoration> {
    return this.http.put<Majoration>(this.url(`/api/majorations/${id}`), dto);
  }
}

@Injectable({ providedIn: 'root' })
export class AbsenceCodesService extends ApiService {
  list(): Observable<AbsenceCode[]> {
    return this.http.get<AbsenceCode[]>(this.url('/api/absence-codes'));
  }
  create(dto: AbsenceCode): Observable<AbsenceCode> {
    return this.http.post<AbsenceCode>(this.url('/api/absence-codes'), dto);
  }
  update(id: number, dto: AbsenceCode): Observable<AbsenceCode> {
    return this.http.put<AbsenceCode>(this.url(`/api/absence-codes/${id}`), dto);
  }
  syncSage(): Observable<void> {
    return this.http.post<void>(this.url('/api/absence-codes/sync-sage'), {});
  }
}

@Injectable({ providedIn: 'root' })
export class CodeConstantesService extends ApiService {
  list(): Observable<CodeConstante[]> {
    return this.http.get<CodeConstante[]>(this.url('/api/code-constantes'));
  }
  update(id: number, dto: CodeConstante): Observable<CodeConstante> {
    return this.http.put<CodeConstante>(this.url(`/api/code-constantes/${id}`), dto);
  }
  sageOptions(): Observable<SageConstantOption[]> {
    return this.http.get<SageConstantOption[]>(this.url('/api/code-constantes/sage-options'));
  }
}
