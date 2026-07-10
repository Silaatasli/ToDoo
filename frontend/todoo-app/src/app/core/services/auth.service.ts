import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, tap } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AuthResult, AuthUser } from '../../models/auth.model';

const TOKEN_KEY = 'todoo_token';
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

  logout(): void {
    sessionStorage.removeItem(TOKEN_KEY);
    sessionStorage.removeItem(USER_KEY);
  }

  getToken(): string | null {
    return sessionStorage.getItem(TOKEN_KEY);
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
  }
}
