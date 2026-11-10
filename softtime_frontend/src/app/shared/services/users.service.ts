import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from '../../core/api';
import { RoleChangePreview } from '../../core/error.logic';
import { DbAccess, DepartementService, DepartementServiceOptions, Droit, PagedResult, Privilege, Role, RoleDetailedPermissions, SageDb, User } from '../models';

@Injectable({ providedIn: 'root' })
export class UsersService extends ApiService {
  list(): Observable<User[]> {
    return this.http.get<User[]>(this.url('/api/v1/users'));
  }
  listPaged(page: number, pageSize: number, search?: string | null): Observable<PagedResult<User>> {
    return this.http.get<PagedResult<User>>(this.url('/api/v1/users/paged'), {
      params: this.params({ page, pageSize, search }),
    });
  }
  get(id: number): Observable<User> {
    return this.http.get<User>(this.url(`/api/v1/users/${id}`));
  }
  create(dto: User): Observable<User> {
    return this.http.post<User>(this.url('/api/v1/users'), dto);
  }
  update(id: number, dto: User): Observable<User> {
    return this.http.put<User>(this.url(`/api/v1/users/${id}`), dto);
  }
  /** Aperçu d'un changement de rôle : ajustements personnels qui seraient effacés (lecture seule). */
  rolePreview(id: number, newRoleId: number): Observable<RoleChangePreview> {
    return this.http.post<RoleChangePreview>(this.url(`/api/v1/users/${id}/role-change-preview`), { newRoleId });
  }
  remove(id: number): Observable<void> {
    return this.http.delete<void>(this.url(`/api/v1/users/${id}`));
  }
}

@Injectable({ providedIn: 'root' })
export class RolesService extends ApiService {
  list(): Observable<Role[]> {
    return this.http.get<Role[]>(this.url('/api/v1/roles'));
  }
  listPaged(page: number, pageSize: number, search?: string | null): Observable<PagedResult<Role>> {
    return this.http.get<PagedResult<Role>>(this.url('/api/v1/roles/paged'), {
      params: this.params({ page, pageSize, search }),
    });
  }
  create(dto: Role): Observable<Role> {
    return this.http.post<Role>(this.url('/api/v1/roles'), dto);
  }
  update(id: number, dto: Role): Observable<Role> {
    return this.http.put<Role>(this.url(`/api/v1/roles/${id}`), dto);
  }
  remove(id: number): Observable<void> {
    return this.http.delete<void>(this.url(`/api/v1/roles/${id}`));
  }
}

@Injectable({ providedIn: 'root' })
export class PrivilegesService extends ApiService {
  droits(): Observable<Droit[]> {
    return this.http.get<Droit[]>(this.url('/api/v1/privileges/droits'));
  }
  byRole(roleId: number): Observable<Privilege[]> {
    return this.http.get<Privilege[]>(this.url('/api/v1/privileges'), {
      params: this.params({ roleId }),
    });
  }
  replace(roleId: number, droitIds: number[]): Observable<void> {
    return this.http.put<void>(this.url(`/api/v1/privileges/${roleId}`), droitIds);
  }
  byUser(userId: number): Observable<{ userId: number; grantedPermissions: string[]; isCustomized: boolean }> {
    return this.http.get<{ userId: number; grantedPermissions: string[]; isCustomized: boolean }>(
      this.url(`/api/v1/privileges/user/${userId}`),
    );
  }
  saveUserPermissions(userId: number, permissions: string[]): Observable<void> {
    return this.http.put<void>(this.url(`/api/v1/privileges/user/${userId}`), {
      userId,
      grantedPermissions: permissions,
    });
  }
  getRolePermissions(roleId: number): Observable<RoleDetailedPermissions> {
    return this.http.get<RoleDetailedPermissions>(this.url(`/api/v1/privileges/role/${roleId}`));
  }
  saveRolePermissions(roleId: number, grantedPermissions: string[]): Observable<void> {
    return this.http.put<void>(this.url(`/api/v1/privileges/role/${roleId}`), { grantedPermissions });
  }
  resetUserPermissions(userId: number): Observable<void> {
    return this.http.post<void>(this.url(`/api/v1/privileges/user/${userId}/reset`), {});
  }
}

@Injectable({ providedIn: 'root' })
export class DbAccessService extends ApiService {
  byUser(userId: number): Observable<DbAccess[]> {
    return this.http.get<DbAccess[]>(this.url('/api/v1/db-access'), {
      params: this.params({ userId }),
    });
  }
  replace(userId: number, sageDbIds: number[]): Observable<void> {
    return this.http.put<void>(this.url(`/api/v1/db-access/${userId}`), sageDbIds);
  }
}

@Injectable({ providedIn: 'root' })
export class SageDatabasesService extends ApiService {
  list(): Observable<SageDb[]> {
    return this.http.get<SageDb[]>(this.url('/api/v1/sage-databases'));
  }
  listPaged(page: number, pageSize: number, search?: string | null): Observable<PagedResult<SageDb>> {
    return this.http.get<PagedResult<SageDb>>(this.url('/api/v1/sage-databases/paged'), {
      params: this.params({ page, pageSize, search }),
    });
  }
  create(dto: SageDb): Observable<SageDb> {
    return this.http.post<SageDb>(this.url('/api/v1/sage-databases'), dto);
  }
  update(id: number, dto: SageDb): Observable<SageDb> {
    return this.http.put<SageDb>(this.url(`/api/v1/sage-databases/${id}`), dto);
  }
  remove(id: number): Observable<void> {
    return this.http.delete<void>(this.url(`/api/v1/sage-databases/${id}`));
  }
  testConnection(dto: Partial<SageDb>): Observable<import('../models').ConnectionTestResult> {
    return this.http.post<import('../models').ConnectionTestResult>(this.url('/api/v1/sage-databases/test-connection'), dto);
  }
  lookupBatch(matricules: string[]): Observable<DepartementService[]> {
    return this.http.post<DepartementService[]>(this.url('/api/v1/sage-databases/lookup-batch'), matricules);
  }
  departementServiceOptions(): Observable<DepartementServiceOptions> {
    return this.http.get<DepartementServiceOptions>(this.url('/api/v1/sage-databases/departement-service-options'));
  }
    activate(): Observable<{ holidaysImported: boolean; cardsAdded: number; cardsDeactivated: number; message: string }> {
    return this.http.post<{ holidaysImported: boolean; cardsAdded: number; cardsDeactivated: number; message: string }>(
      this.url('/api/v1/sage-databases/activate'), {});
  }
}
