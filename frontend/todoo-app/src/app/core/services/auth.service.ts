import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError, of, tap } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AuthResult, AuthUser } from '../../models/auth.model';
import { getUserIdFromToken } from '../utils/jwt.utils';

const TOKEN_KEY = 'todoo_token';
const REFRESH_TOKEN_KEY = 'todoo_refresh_token';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);

  login(email: string, password: string): Observable<AuthResult> {
    return this.http
      .post<AuthResult>(`${environment.apiUrl}/auth/login`, { email, password })
      .pipe(tap((result) => this.persistSession(result)));
  }

  register(firstName: string, lastName: string, email: string, password: string): Observable<AuthResult> {
    return this.http
      .post<AuthResult>(`${environment.apiUrl}/auth/register`, { firstName, lastName, email, password })
      .pipe(tap((result) => this.persistSession(result)));
  }

  forgotPassword(email: string): Observable<AuthResult> {
    return this.http.post<AuthResult>(`${environment.apiUrl}/auth/forgot-password`, { email });
  }

  resetPassword(token: string, newPassword: string): Observable<AuthResult> {
    return this.http.post<AuthResult>(`${environment.apiUrl}/auth/reset-password`, { token, newPassword });
  }

  /**
   * Refresh token ile yeni bir access token alir. Basarili olursa oturumu
   * (access + rotate edilmis refresh token) gunceller.
   */
  refresh(): Observable<AuthResult | null> {
    const refreshToken = this.getRefreshToken();
    if (!refreshToken) {
      return of(null);
    }

    return this.http
      .post<AuthResult>(`${environment.apiUrl}/auth/refresh`, { refreshToken })
      .pipe(
        tap((result) => this.persistSession(result)),
        catchError(() => {
          this.logout();
          return of(null);
        })
      );
  }

  logout(): void {
    const refreshToken = this.getRefreshToken();

    sessionStorage.removeItem(TOKEN_KEY);
    sessionStorage.removeItem(REFRESH_TOKEN_KEY);
    sessionStorage.removeItem('todoo_user');

    if (refreshToken) {
      this.http
        .post(`${environment.apiUrl}/auth/logout`, { refreshToken })
        .pipe(catchError(() => of(null)))
        .subscribe();
    }
  }

  getToken(): string | null {
    return sessionStorage.getItem(TOKEN_KEY);
  }

  getRefreshToken(): string | null {
    return sessionStorage.getItem(REFRESH_TOKEN_KEY);
  }

  getUser(): AuthUser | null {
    const token = this.getToken();
    if (!token) {
      return null;
    }

    const userId = getUserIdFromToken(token);
    return userId == null ? null : { userId };
  }

  getUserId(): number | null {
    return this.getUser()?.userId ?? null;
  }

  isLoggedIn(): boolean {
    return !!this.getToken();
  }

  private persistSession(result: AuthResult): void {
    if (!result.success || !result.token) {
      return;
    }

    sessionStorage.setItem(TOKEN_KEY, result.token);
    sessionStorage.removeItem('todoo_user');

    if (result.refreshToken) {
      sessionStorage.setItem(REFRESH_TOKEN_KEY, result.refreshToken);
    }
  }
}
