import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError, of, tap } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AuthResult, AuthUser } from '../../models/auth.model';

const TOKEN_KEY = 'todoo_token';
const REFRESH_TOKEN_KEY = 'todoo_refresh_token';
const USER_KEY = 'todoo_user';

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
    sessionStorage.removeItem(USER_KEY);

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
    const raw = sessionStorage.getItem(USER_KEY);
    if (!raw) {
      return null;
    }

    try {
      return JSON.parse(raw) as AuthUser;
    } catch {
      return null;
    }
  }

  isLoggedIn(): boolean {
    return !!this.getToken();
  }

  private persistSession(result: AuthResult): void {
    if (!result.success || !result.token || !result.userId || !result.email) {
      return;
    }

    sessionStorage.setItem(TOKEN_KEY, result.token);
    sessionStorage.setItem(
      USER_KEY,
      JSON.stringify({ userId: result.userId, email: result.email } satisfies AuthUser)
    );

    if (result.refreshToken) {
      sessionStorage.setItem(REFRESH_TOKEN_KEY, result.refreshToken);
    }
  }
}
