import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { BehaviorSubject, catchError, filter, switchMap, take, throwError } from 'rxjs';
import { AuthService } from '../services/auth.service';

// Modul seviyesinde tutulur: ayni anda birden fazla istek 401 alirsa
// sadece tek bir refresh cagrisi yapilir, digerleri sonucu bekler.
let isRefreshing = false;
const refreshedToken$ = new BehaviorSubject<string | null>(null);

const AUTH_ENDPOINTS = ['/auth/login', '/auth/register', '/auth/refresh'];

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  const token = auth.getToken();

  const authReq = token
    ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } })
    : req;

  if (AUTH_ENDPOINTS.some((endpoint) => req.url.includes(endpoint))) {
    return next(authReq);
  }

  return next(authReq).pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse) || error.status !== 401 || !auth.getRefreshToken()) {
        return throwError(() => error);
      }

      if (isRefreshing) {
        return refreshedToken$.pipe(
          filter((newToken) => newToken !== null),
          take(1),
          switchMap((newToken) => next(req.clone({ setHeaders: { Authorization: `Bearer ${newToken}` } })))
        );
      }

      isRefreshing = true;
      refreshedToken$.next(null);

      return auth.refresh().pipe(
        switchMap((result) => {
          isRefreshing = false;

          if (!result?.token) {
            router.navigate(['/login']);
            return throwError(() => error);
          }

          refreshedToken$.next(result.token);
          return next(req.clone({ setHeaders: { Authorization: `Bearer ${result.token}` } }));
        }),
        catchError((refreshError) => {
          isRefreshing = false;
          router.navigate(['/login']);
          return throwError(() => refreshError);
        })
      );
    })
  );
};
