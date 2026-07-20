import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Observable, tap } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AppNotification, NotificationListResponse } from '../../models/notification.model';

@Injectable({ providedIn: 'root' })
export class NotificationService {
  private readonly http = inject(HttpClient);

  readonly items = signal<AppNotification[]>([]);
  readonly unreadCount = signal(0);
  readonly toasts = signal<AppNotification[]>([]);

  load(): void {
    this.http.get<NotificationListResponse>(`${environment.apiUrl}/notifications`).subscribe({
      next: (result) => {
        this.items.set(result.items ?? []);
        this.unreadCount.set(result.unreadCount ?? 0);
      },
      error: () => {
        this.items.set([]);
        this.unreadCount.set(0);
      }
    });
  }

  markRead(id: string): Observable<{ success: boolean; unreadCount: number }> {
    return this.http
      .post<{ success: boolean; unreadCount: number }>(`${environment.apiUrl}/notifications/${id}/read`, {})
      .pipe(
        tap((result) => {
          this.unreadCount.set(result.unreadCount ?? 0);
          this.items.update((list) =>
            list.map((item) => (item.id === id ? { ...item, isRead: true } : item))
          );
        })
      );
  }

  markAllRead(): Observable<{ success: boolean; unreadCount: number }> {
    return this.http
      .post<{ success: boolean; unreadCount: number }>(`${environment.apiUrl}/notifications/read-all`, {})
      .pipe(
        tap(() => {
          this.unreadCount.set(0);
          this.items.update((list) => list.map((item) => ({ ...item, isRead: true })));
        })
      );
  }

  markReadMany(ids: string[]): Observable<{ success: boolean; unreadCount: number }> {
    return this.http
      .post<{ success: boolean; unreadCount: number }>(`${environment.apiUrl}/notifications/read-many`, { ids })
      .pipe(
        tap((result) => {
          this.unreadCount.set(result.unreadCount ?? 0);
          const idSet = new Set(ids);
          this.items.update((list) =>
            list.map((item) => (idSet.has(item.id) ? { ...item, isRead: true } : item))
          );
        })
      );
  }

  delete(id: string): Observable<{ success: boolean; unreadCount: number }> {
    return this.http
      .delete<{ success: boolean; unreadCount: number }>(`${environment.apiUrl}/notifications/${id}`)
      .pipe(
        tap((result) => {
          this.unreadCount.set(result.unreadCount ?? 0);
          this.items.update((list) => list.filter((item) => item.id !== id));
        })
      );
  }

  deleteMany(ids: string[]): Observable<{ success: boolean; deleted: number; unreadCount: number }> {
    return this.http
      .post<{ success: boolean; deleted: number; unreadCount: number }>(
        `${environment.apiUrl}/notifications/delete-many`,
        { ids }
      )
      .pipe(
        tap((result) => {
          this.unreadCount.set(result.unreadCount ?? 0);
          const idSet = new Set(ids);
          this.items.update((list) => list.filter((item) => !idSet.has(item.id)));
        })
      );
  }

  clear(): Observable<{ success: boolean; unreadCount: number }> {
    return this.http
      .post<{ success: boolean; unreadCount: number }>(`${environment.apiUrl}/notifications/clear`, {})
      .pipe(
        tap(() => {
          this.unreadCount.set(0);
          this.items.set([]);
        })
      );
  }

  pushRealtime(notification: AppNotification, unreadCount: number): void {
    this.unreadCount.set(unreadCount);
    this.items.update((list) => [notification, ...list.filter((item) => item.id !== notification.id)].slice(0, 50));
    this.showToast(notification);
  }

  showToast(notification: AppNotification): void {
    this.toasts.update((list) => [notification, ...list].slice(0, 3));
    window.setTimeout(() => this.dismissToast(notification.id), 5000);
  }

  dismissToast(id: string): void {
    this.toasts.update((list) => list.filter((item) => item.id !== id));
  }
}
