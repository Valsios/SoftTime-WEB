import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from '../../core/api';
import { DbAccess, Droit, Privilege, Role, SageDb, User } from '../models';

@Injectable({ providedIn: 'root' })
export class UsersService extends ApiService {
  list(): Observable<User[]> {
    return this.http.get<User[]>(this.url('/api/users'));
  }
  get(id: number): Observable<User> {
    return this.http.get<User>(this.url(`/api/users/${id}`));
  }
  create(dto: User): Observable<User> {
    return this.http.post<User>(this.url('/api/users'), dto);
  }
  update(id: number, dto: User): Observable<User> {
    return this.http.put<User>(this.url(`/api/users/${id}`), dto);
  }
  remove(id: number): Observable<void> {
    return this.http.delete<void>(this.url(`/api/users/${id}`));
  }
}

@Injectable({ providedIn: 'root' })
export class RolesService extends ApiService {
  list(): Observable<Role[]> {
    return this.http.get<Role[]>(this.url('/api/roles'));
  }
  create(dto: Role): Observable<Role> {
    return this.http.post<Role>(this.url('/api/roles'), dto);
  }
  update(id: number, dto: Role): Observable<Role> {
    return this.http.put<Role>(this.url(`/api/roles/${id}`), dto);
  }
  remove(id: number): Observable<void> {
    return this.http.delete<void>(this.url(`/api/roles/${id}`));
  }
}

@Injectable({ providedIn: 'root' })
export class PrivilegesService extends ApiService {
  droits(): Observable<Droit[]> {
    return this.http.get<Droit[]>(this.url('/api/privileges/droits'));
  }
  byRole(roleId: number): Observable<Privilege[]> {
    return this.http.get<Privilege[]>(this.url('/api/privileges'), {
      params: this.params({ roleId }),
    });
  }
  replace(roleId: number, droitIds: number[]): Observable<void> {
    return this.http.put<void>(this.url(`/api/privileges/${roleId}`), droitIds);
  }
}

@Injectable({ providedIn: 'root' })
export class DbAccessService extends ApiService {
  byUser(userId: number): Observable<DbAccess[]> {
    return this.http.get<DbAccess[]>(this.url('/api/db-access'), {
      params: this.params({ userId }),
    });
  }
  replace(userId: number, sageDbIds: number[]): Observable<void> {
    return this.http.put<void>(this.url(`/api/db-access/${userId}`), sageDbIds);
  }
}

@Injectable({ providedIn: 'root' })
export class SageDatabasesService extends ApiService {
  list(): Observable<SageDb[]> {
    return this.http.get<SageDb[]>(this.url('/api/sage-databases'));
  }
  create(dto: SageDb): Observable<SageDb> {
    return this.http.post<SageDb>(this.url('/api/sage-databases'), dto);
  }
  update(id: number, dto: SageDb): Observable<SageDb> {
    return this.http.put<SageDb>(this.url(`/api/sage-databases/${id}`), dto);
  }
  remove(id: number): Observable<void> {
    return this.http.delete<void>(this.url(`/api/sage-databases/${id}`));
  }
}
