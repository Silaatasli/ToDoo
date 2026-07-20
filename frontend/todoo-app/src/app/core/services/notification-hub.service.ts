import { Injectable, inject } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { AuthService } from './auth.service';
import { NotificationService } from './notification.service';
import { environment } from '../../../environments/environment';
import { NotificationReceivedPayload } from '../../models/notification.model';

@Injectable({ providedIn: 'root' })
export class NotificationHubService {
  private readonly auth = inject(AuthService);
  private readonly notifications = inject(NotificationService);

  private connection: signalR.HubConnection | null = null;
  private browserPermissionRequested = false;

  async connect(): Promise<void> {
    if (this.connection) {
      return;
    }

    this.connection = new signalR.HubConnectionBuilder()
      .withUrl(environment.notificationHubUrl, {
        accessTokenFactory: () => this.auth.getToken() ?? ''
      })
      .withAutomaticReconnect()
      .build();

    this.connection.on('NotificationReceived', (payload: NotificationReceivedPayload) => {
      if (!payload?.notification) {
        return;
      }

      this.notifications.pushRealtime(payload.notification, payload.unreadCount ?? 0);

      if (typeof document !== 'undefined' && document.hidden) {
        this.showBrowserNotification(payload.notification.title, payload.notification.body, payload.notification);
      }
    });

    await this.connection.start();
    void this.ensureBrowserPermission();
  }

  async disconnect(): Promise<void> {
    if (!this.connection) {
      return;
    }

    const conn = this.connection;
    this.connection = null;
    try {
      await conn.stop();
    } catch {
      // ignore
    }
  }

  private async ensureBrowserPermission(): Promise<void> {
    if (this.browserPermissionRequested || typeof Notification === 'undefined') {
      return;
    }

    this.browserPermissionRequested = true;
    if (Notification.permission === 'default') {
      try {
        await Notification.requestPermission();
      } catch {
        // ignore
      }
    }
  }

  private showBrowserNotification(
    title: string,
    body: string,
    notification: NotificationReceivedPayload['notification']
  ): void {
    if (typeof Notification === 'undefined' || Notification.permission !== 'granted') {
      return;
    }

    const browserNotif = new Notification(title, {
      body,
      tag: notification.id
    });

    browserNotif.onclick = () => {
      window.focus();
      browserNotif.close();
      window.dispatchEvent(new CustomEvent('todoo-notification-click', { detail: notification }));
    };
  }
}
