import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { SessionStore } from '../session.store';

/** Adds the JWT bearer token and tenant headers to every API request except login. */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  if (req.url.includes('/api/auth/login')) {
    return next(req);
  }

  const session = inject(SessionStore);
  const token = session.token();
  const sage = session.activeSageDb();
  const pointeuse = session.activePointeuseDb();

  let headers = req.headers;
  if (token) {
    headers = headers.set('Authorization', `Bearer ${token}`);
  }
  if (sage) {
    headers = headers.set('X-Sage-Database', sage);
  }
  if (pointeuse) {
    headers = headers.set('X-Pointeuse-Database', pointeuse);
  }

  return next(req.clone({ headers }));
};
