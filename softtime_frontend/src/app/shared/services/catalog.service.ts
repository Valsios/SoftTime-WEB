import { HttpContext } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable, tap } from 'rxjs';
import { ApiService } from '../../core/api';
import { SessionStore } from '../../core/session.store';
import { silentContext } from '../../core/silent-request';
import {
  AbsenceCode,
  Affectation,
  AffectationOptions,
  AffectationQuery,
  AffectationRow,
  CardPaie,
  Category,
  ClockParam,
  CodeConstante,
  CorrespondenceMode,
  Holiday,
  ImportResult,
  Majoration,
  PagedResult,
  PointeuseDb,
  SageConstantOption,
  Tolerance,
  DepartementService,
  ConnectionTestResult,
  FieldRole,
  PayrollWriteConfiguration,
} from '../models';

/** Options d'une lecture : contexte silencieux si demandé (voir silent-request.ts). */
function requestOptions(options: { silent?: boolean }): { context?: HttpContext } {
  return options.silent ? { context: silentContext() } : {};
}

@Injectable({ providedIn: 'root' })
export class PointeuseDatabasesService extends ApiService {
  private readonly session = inject(SessionStore);

  list(): Observable<PointeuseDb[]> {
    return this.http.get<PointeuseDb[]>(this.url('/api/v1/pointeuse-databases'));
  }
  listPaged(page: number, pageSize: number, search?: string | null): Observable<PagedResult<PointeuseDb>> {
    return this.http.get<PagedResult<PointeuseDb>>(this.url('/api/v1/pointeuse-databases/paged'), {
      params: this.params({ page, pageSize, search }),
    });
  }
  refresh(): Observable<PointeuseDb[]> {
    return this.list().pipe(
      tap((list) => this.session.setPointeuseDatabases(list)),
    );
  }
  create(dto: PointeuseDb): Observable<PointeuseDb> {
    return this.http.post<PointeuseDb>(this.url('/api/v1/pointeuse-databases'), dto);
  }
  update(id: number, dto: PointeuseDb): Observable<PointeuseDb> {
    return this.http.put<PointeuseDb>(this.url(`/api/v1/pointeuse-databases/${id}`), dto);
  }
  remove(id: number): Observable<void> {
    return this.http.delete<void>(this.url(`/api/v1/pointeuse-databases/${id}`));
  }
  testConnection(dto: Partial<PointeuseDb>): Observable<ConnectionTestResult> {
    return this.http.post<ConnectionTestResult>(this.url('/api/v1/pointeuse-databases/test-connection'), dto);
  }
}

@Injectable({ providedIn: 'root' })
export class SageDatabasesService extends ApiService {
  activate(): Observable<{ holidaysImported: boolean; cardsAdded: number; cardsDeactivated: number; message: string }> {
    return this.http.post<{ holidaysImported: boolean; cardsAdded: number; cardsDeactivated: number; message: string }>(
      this.url('/api/v1/sage-databases/activate'), {});
  }
}
@Injectable({ providedIn: 'root' })
export class FieldRolesService extends ApiService {
  list(systemType?: 'SAGE' | 'POINTEUSE'): Observable<FieldRole[]> {
    return this.http.get<FieldRole[]>(this.url('/api/v1/field-roles'), {
      params: this.params({ systemType }),
    });
  }
}
@Injectable({ providedIn: 'root' })
export class DiscoveryService extends ApiService {
  servers(): Observable<string[]> {
    return this.http.get<string[]>(this.url('/api/v1/discovery/servers'));
  }
  databases(dto: { serveur: string; sqlAuth: boolean; login: string | null; password: string | null; connectionId?: number; connectionKind?: 'SAGE' | 'POINTEUSE' }): Observable<string[]> {
    return this.http.post<string[]>(this.url('/api/v1/discovery/databases'), dto);
  }
  tables(dto: { serveur: string; base: string; sqlAuth: boolean; login: string | null; password: string | null; connectionId?: number; connectionKind?: 'SAGE' | 'POINTEUSE' }): Observable<string[]> {
    return this.http.post<string[]>(this.url('/api/v1/discovery/tables'), dto);
  }
  columns(dto: { serveur: string; base: string; table: string; sqlAuth: boolean; login: string | null; password: string | null; connectionId?: number; connectionKind?: 'SAGE' | 'POINTEUSE' }): Observable<string[]> {
    return this.http.post<string[]>(this.url('/api/v1/discovery/columns'), dto);
  }
}

@Injectable({ providedIn: 'root' })
export class ClockParamsService extends ApiService {
  /** silent : affichage d'information (autre écran que celui des paramètres), un échec n'affiche aucun toast. */
  get(options: { silent?: boolean } = {}): Observable<ClockParam> {
    return this.http.get<ClockParam>(this.url('/api/v1/clock-params'), requestOptions(options));
  }
  update(dto: ClockParam): Observable<ClockParam> {
    return this.http.put<ClockParam>(this.url('/api/v1/clock-params'), dto);
  }
}

@Injectable({ providedIn: 'root' })
export class CorrespondenceModeService extends ApiService {
  get(): Observable<CorrespondenceMode> {
    return this.http.get<CorrespondenceMode>(this.url('/api/v1/correspondence-mode'));
  }
  update(dto: CorrespondenceMode): Observable<CorrespondenceMode> {
    return this.http.put<CorrespondenceMode>(this.url('/api/v1/correspondence-mode'), dto);
  }
}

@Injectable({ providedIn: 'root' })
export class CardPaieService extends ApiService {
  /** silent : liste qui remplit un filtre (matricules, départements), un échec n'affiche aucun toast. */
  list(options: { silent?: boolean } = {}): Observable<CardPaie[]> {
    return this.http.get<CardPaie[]>(this.url('/api/v1/card-paie'), requestOptions(options));
  }
  listPaged(page: number, pageSize: number, search?: string | null): Observable<PagedResult<CardPaie>> {
    return this.http.get<PagedResult<CardPaie>>(this.url('/api/v1/card-paie/paged'), {
      params: this.params({ page, pageSize, search }),
    });
  }
  create(dto: CardPaie): Observable<CardPaie> {
    return this.http.post<CardPaie>(this.url('/api/v1/card-paie'), dto);
  }
  update(id: number, dto: CardPaie): Observable<CardPaie> {
    return this.http.put<CardPaie>(this.url(`/api/v1/card-paie/${id}`), dto);
  }
  remove(id: number): Observable<void> {
    return this.http.delete<void>(this.url(`/api/v1/card-paie/${id}`));
  }
    autoMap(): Observable<{ added: number; deactivated: number; message: string }> {
    return this.http.post<{ added: number; deactivated: number; message: string }>(this.url('/api/v1/card-paie/auto-map'), {});
  }
  importCsv(file: File): Observable<ImportResult> {
    const form = new FormData();
    form.append('file', file);
    return this.http.post<ImportResult>(this.url('/api/v1/card-paie/import-csv'), form);
  }
}

@Injectable({ providedIn: 'root' })
export class CategoriesService extends ApiService {
  list(): Observable<Category[]> {
    return this.http.get<Category[]>(this.url('/api/v1/categories'));
  }
  listPaged(page: number, pageSize: number, search?: string | null): Observable<PagedResult<Category>> {
    return this.http.get<PagedResult<Category>>(this.url('/api/v1/categories/paged'), {
      params: this.params({ page, pageSize, search }),
    });
  }
  create(dto: Category): Observable<Category> {
    return this.http.post<Category>(this.url('/api/v1/categories'), dto);
  }
  update(id: number, dto: Category): Observable<Category> {
    return this.http.put<Category>(this.url(`/api/v1/categories/${id}`), dto);
  }
  remove(id: number): Observable<void> {
    return this.http.delete<void>(this.url(`/api/v1/categories/${id}`));
  }
}

@Injectable({ providedIn: 'root' })
export class AffectationsService extends ApiService {
  list(categoryId?: number): Observable<Affectation[]> {
    return this.http.get<Affectation[]>(this.url('/api/v1/affectations'), {
      params: this.params({ categoryId }),
    });
  }
  listPaged(query: AffectationQuery & { page: number; pageSize: number }): Observable<PagedResult<AffectationRow>> {
    return this.http.get<PagedResult<AffectationRow>>(this.url('/api/v1/affectations/paged'), {
      params: this.params({ ...query }),
    });
  }
  listAll(query?: AffectationQuery): Observable<AffectationRow[]> {
    return this.http.get<AffectationRow[]>(this.url('/api/v1/affectations/all'), {
      params: this.params({ ...(query ?? {}) }),
    });
  }
  options(): Observable<AffectationOptions> {
    return this.http.get<AffectationOptions>(this.url('/api/v1/affectations/options'));
  }
  create(dto: Affectation): Observable<Affectation> {
    return this.http.post<Affectation>(this.url('/api/v1/affectations'), dto);
  }
  update(id: number, dto: Affectation): Observable<Affectation> {
    return this.http.put<Affectation>(this.url(`/api/v1/affectations/${id}`), dto);
  }
  remove(id: number): Observable<void> {
    return this.http.delete<void>(this.url(`/api/v1/affectations/${id}`));
  }
  importExcel(file: File): Observable<ImportResult> {
    const form = new FormData();
    form.append('file', file);
    return this.http.post<ImportResult>(this.url('/api/v1/affectations/import-excel'), form);
  }
}

@Injectable({ providedIn: 'root' })
export class TolerancesService extends ApiService {
  list(categoryId?: number): Observable<Tolerance[]> {
    return this.http.get<Tolerance[]>(this.url('/api/v1/tolerances'), {
      params: this.params({ categoryId }),
    });
  }
  listPaged(page: number, pageSize: number, categoryId?: number, search?: string | null): Observable<PagedResult<Tolerance>> {
    return this.http.get<PagedResult<Tolerance>>(this.url('/api/v1/tolerances/paged'), {
      params: this.params({ page, pageSize, categoryId, search }),
    });
  }
  create(dto: Tolerance): Observable<Tolerance> {
    return this.http.post<Tolerance>(this.url('/api/v1/tolerances'), dto);
  }
  update(id: number, dto: Tolerance): Observable<Tolerance> {
    return this.http.put<Tolerance>(this.url(`/api/v1/tolerances/${id}`), dto);
  }
  remove(id: number): Observable<void> {
    return this.http.delete<void>(this.url(`/api/v1/tolerances/${id}`));
  }
}

@Injectable({ providedIn: 'root' })
export class HolidaysService extends ApiService {
  list(): Observable<Holiday[]> {
    return this.http.get<Holiday[]>(this.url('/api/v1/holidays'));
  }
  listPaged(page: number, pageSize: number, search?: string | null): Observable<PagedResult<Holiday>> {
    return this.http.get<PagedResult<Holiday>>(this.url('/api/v1/holidays/paged'), {
      params: this.params({ page, pageSize, search }),
    });
  }
  create(dto: Holiday): Observable<Holiday> {
    return this.http.post<Holiday>(this.url('/api/v1/holidays'), dto);
  }
  update(id: number, dto: Holiday): Observable<Holiday> {
    return this.http.put<Holiday>(this.url(`/api/v1/holidays/${id}`), dto);
  }
  remove(id: number): Observable<void> {
    return this.http.delete<void>(this.url(`/api/v1/holidays/${id}`));
  }
  importSage(): Observable<void> {
    return this.http.post<void>(this.url('/api/v1/holidays/import-sage'), {});
  }
}

@Injectable({ providedIn: 'root' })
export class MajorationsService extends ApiService {
  list(): Observable<Majoration[]> {
    return this.http.get<Majoration[]>(this.url('/api/v1/majorations'));
  }
  listPaged(page: number, pageSize: number, search?: string | null): Observable<PagedResult<Majoration>> {
    return this.http.get<PagedResult<Majoration>>(this.url('/api/v1/majorations/paged'), {
      params: this.params({ page, pageSize, search }),
    });
  }
  update(id: number, dto: Majoration): Observable<Majoration> {
    return this.http.put<Majoration>(this.url(`/api/v1/majorations/${id}`), dto);
  }
}

@Injectable({ providedIn: 'root' })
export class AbsenceCodesService extends ApiService {
  list(): Observable<AbsenceCode[]> {
    return this.http.get<AbsenceCode[]>(this.url('/api/v1/absence-codes'));
  }
  listPaged(page: number, pageSize: number, search?: string | null): Observable<PagedResult<AbsenceCode>> {
    return this.http.get<PagedResult<AbsenceCode>>(this.url('/api/v1/absence-codes/paged'), {
      params: this.params({ page, pageSize, search }),
    });
  }
  create(dto: AbsenceCode): Observable<AbsenceCode> {
    return this.http.post<AbsenceCode>(this.url('/api/v1/absence-codes'), dto);
  }
  update(id: number, dto: AbsenceCode): Observable<AbsenceCode> {
    return this.http.put<AbsenceCode>(this.url(`/api/v1/absence-codes/${id}`), dto);
  }
  syncSage(): Observable<void> {
    return this.http.post<void>(this.url('/api/v1/absence-codes/sync-sage'), {});
  }
}

@Injectable({ providedIn: 'root' })
export class DepartementServiceService extends ApiService {
  /**
   * Recherche le departement/service Sage pour une liste de matricules (complément du filtre de période) : un échec
   * n'affiche aucun toast. Réservée à la porte 2 avec PARAMS:CARDPAIE côté serveur.
   */
  lookupBatch(matricules: string[]): Observable<DepartementService[]> {
    return this.http.post<DepartementService[]>(this.url('/api/v1/departement-service/lookup-batch'), matricules, {
      context: silentContext(),
    });
  }
}

@Injectable({ providedIn: 'root' })
export class CodeConstantesService extends ApiService {
  list(): Observable<CodeConstante[]> {
    return this.http.get<CodeConstante[]>(this.url('/api/v1/code-constantes'));
  }
  listPaged(page: number, pageSize: number, search?: string | null): Observable<PagedResult<CodeConstante>> {
    return this.http.get<PagedResult<CodeConstante>>(this.url('/api/v1/code-constantes/paged'), {
      params: this.params({ page, pageSize, search }),
    });
  }
  update(id: number, dto: CodeConstante): Observable<CodeConstante> {
    return this.http.put<CodeConstante>(this.url(`/api/v1/code-constantes/${id}`), dto);
  }
  sageOptions(): Observable<SageConstantOption[]> {
    return this.http.get<SageConstantOption[]>(this.url('/api/v1/code-constantes/sage-options'));
  }
  /** Tables candidates pour un code constante de base AUTRE (table du mapping CONSTANT). */
  otherTables(): Observable<string[]> {
    return this.http.get<string[]>(this.url('/api/v1/code-constantes/autre-tables'));
  }
  /** Valeurs distinctes de la colonne de code constante mappee pour une base AUTRE. */
  otherValues(table: string): Observable<string[]> {
    return this.http.get<string[]>(this.url('/api/v1/code-constantes/autre-values'), {
      params: this.params({ table }),
    });
  }
}

@Injectable({ providedIn: 'root' })
export class PayrollWriteConfigurationService extends ApiService {
  list(): Observable<PayrollWriteConfiguration[]> {
    return this.http.get<PayrollWriteConfiguration[]>(this.url('/api/v1/payroll-write-config'));
  }
  create(dto: PayrollWriteConfiguration): Observable<PayrollWriteConfiguration> {
    return this.http.post<PayrollWriteConfiguration>(this.url('/api/v1/payroll-write-config'), dto);
  }
  update(id: number, dto: PayrollWriteConfiguration): Observable<PayrollWriteConfiguration> {
    return this.http.put<PayrollWriteConfiguration>(this.url(`/api/v1/payroll-write-config/${id}`), dto);
  }
  remove(id: number): Observable<void> {
    return this.http.delete<void>(this.url(`/api/v1/payroll-write-config/${id}`));
  }
  test(): Observable<ConnectionTestResult> {
    return this.http.post<ConnectionTestResult>(this.url('/api/v1/payroll-write-config/test'), {});
  }
}
