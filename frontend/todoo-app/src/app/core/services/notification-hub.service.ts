import { Injectable, NgZone, inject } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { AuthService } from './auth.service';
import { NotificationService } from './notification.service';
import { environment } from '../../../environments/environment';
import { AppNotification, NotificationReceivedPayload } from '../../models/notification.model';

@Injectable({ providedIn: 'root' })
export class NotificationHubService {
  private readonly auth = inject(AuthService);
  private readonly notifications = inject(NotificationService);
  private readonly ngZone = inject(NgZone);

  private connection: signalR.HubConnection | null = null;
  private browserPermissionRequested = false;
  private starting: Promise<void> | null = null;
  private intentionallyStopped = false;
  private lifecycleBound = false;
  private reconnectTimer: ReturnType<typeof setTimeout> | null = null;

  async connect(): Promise<void> {
    if (!this.auth.isLoggedIn()) {
      return;
    }

    this.intentionallyStopped = false;
    this.bindLifecycleListeners();

    if (this.connection?.state === signalR.HubConnectionState.Connected) {
      return;
    }

    if (this.connection?.state === signalR.HubConnectionState.Connecting
      || this.connection?.state === signalR.HubConnectionState.Reconnecting) {
      return;
    }

    if (this.starting) {
      await this.starting;
      return;
    }

    this.starting = this.startConnection();
    try {
      await this.starting;
    } finally {
      this.starting = null;
    }
  }

  async disconnect(): Promise<void> {
    this.intentionallyStopped = true;
    this.clearReconnectTimer();
    this.unbindLifecycleListeners();

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

  private async startConnection(): Promise<void> {
    if (this.connection) {
      try {
        await this.connection.stop();
      } catch {
        // ignore
      }
      this.connection = null;
    }

    this.connection = new signalR.HubConnectionBuilder()
      .withUrl(environment.notificationHubUrl, {
        accessTokenFactory: () => this.auth.getToken() ?? ''
      })
      .withAutomaticReconnect({
        nextRetryDelayInMilliseconds: (retryContext) => {
          // Sonsuz retry: 0, 2s, 5s, 10s, sonra max 30s aralikla devam
          const delays = [0, 2000, 5000, 10000];
          if (retryContext.previousRetryCount < delays.length) {
            return delays[retryContext.previousRetryCount];
          }
          return 30000;
        }
      })
      .build();

    // Arka plan sekmelerinde tarayici throttle eder; timeout'u biraz genis tut
    this.connection.serverTimeoutInMilliseconds = 60_000;
    this.connection.keepAliveIntervalInMilliseconds = 15_000;

    this.connection.on('NotificationReceived', (...args: unknown[]) => {
      const payload = this.normalizePayload(args);
      if (!payload?.notification) {
        return;
      }

      this.ngZone.run(() => {
        this.notifications.pushRealtime(payload.notification, payload.unreadCount ?? 0);
      });

      // Yeni takima eklendiyse notification hub takim grubuna da katil.
      if (payload.notification.type === 'TeamMemberAdded' && payload.notification.teamId != null) {
        void this.invokeJoinTeam(payload.notification.teamId);
      }

      if (typeof document !== 'undefined' && document.hidden) {
        this.showBrowserNotification(payload.notification.title, payload.notification.body, payload.notification);
      }
    });

    // Takim broadcast (duyuru vb.): tek SignalR mesaji, inbox Redis'te kisiye ozel.
    this.connection.on('TeamNotificationReceived', (...args: unknown[]) => {
      const payload = this.normalizeTeamPayload(args);
      if (!payload?.notification) {
        return;
      }

      const myId = this.auth.getUserId();
      if (payload.excludeUserId != null && myId != null && payload.excludeUserId === myId) {
        return;
      }

      this.ngZone.run(() => {
        this.notifications.load();
        this.notifications.showToast(payload.notification);
      });

      if (typeof document !== 'undefined' && document.hidden) {
        this.showBrowserNotification(payload.notification.title, payload.notification.body, payload.notification);
      }
    });

    this.connection.onreconnected(() => {
      // OnConnectedAsync takim gruplarini yeniden ekler; ekstra guvence:
      void this.invokeRefreshTeamGroups();
    });

    this.connection.onclose(() => {
      // Automatic reconnect tukendiginde (veya fatal close) buraya duser.
      if (this.intentionallyStopped || !this.auth.isLoggedIn()) {
        return;
      }
      this.scheduleManualReconnect();
    });

    try {
      await this.connection.start();
      void this.ensureBrowserPermission();
    } catch {
      this.connection = null;
      if (!this.intentionallyStopped && this.auth.isLoggedIn()) {
        this.scheduleManualReconnect();
      }
    }
  }

  private scheduleManualReconnect(): void {
    this.clearReconnectTimer();
    this.reconnectTimer = setTimeout(() => {
      this.reconnectTimer = null;
      void this.connect();
    }, 5000);
  }

  private clearReconnectTimer(): void {
    if (this.reconnectTimer !== null) {
      clearTimeout(this.reconnectTimer);
      this.reconnectTimer = null;
    }
  }

  private bindLifecycleListeners(): void {
    if (this.lifecycleBound || typeof window === 'undefined') {
      return;
    }

    this.lifecycleBound = true;
    window.addEventListener('online', this.onOnline);
    document.addEventListener('visibilitychange', this.onVisibilityChange);
  }

  private unbindLifecycleListeners(): void {
    if (!this.lifecycleBound || typeof window === 'undefined') {
      return;
    }

    this.lifecycleBound = false;
    window.removeEventListener('online', this.onOnline);
    document.removeEventListener('visibilitychange', this.onVisibilityChange);
  }

  private readonly onOnline = (): void => {
    void this.ensureConnected();
  };

  private readonly onVisibilityChange = (): void => {
    if (document.visibilityState === 'visible') {
      void this.ensureConnected();
    }
  };

  /** Sekme tekrar aktif / internet gelince bagli degilse yeniden baslat. */
  private async ensureConnected(): Promise<void> {
    if (this.intentionallyStopped || !this.auth.isLoggedIn()) {
      return;
    }

    const state = this.connection?.state;
    if (state === signalR.HubConnectionState.Connected
      || state === signalR.HubConnectionState.Connecting
      || state === signalR.HubConnectionState.Reconnecting) {
      return;
    }

    this.clearReconnectTimer();
    await this.connect();
  }

  private normalizePayload(args: unknown[]): NotificationReceivedPayload | null {
    if (args.length === 0) {
      return null;
    }

    const first = args[0];
    if (first && typeof first === 'object') {
      const record = first as Record<string, unknown>;
      if (record['notification'] && typeof record['notification'] === 'object') {
        return {
          notification: this.normalizeNotification(record['notification']),
          unreadCount: Number(record['unreadCount'] ?? 0)
        };
      }

      // DTO dogrudan gonderildiyse
      if (typeof record['id'] === 'string' && typeof record['title'] === 'string') {
        return {
          notification: this.normalizeNotification(record),
          unreadCount: typeof args[1] === 'number' ? args[1] : 0
        };
      }
    }

    return null;
  }

  private normalizeTeamPayload(args: unknown[]): {
    notification: AppNotification;
    teamId: number | null;
    excludeUserId: number | null;
  } | null {
    if (args.length === 0 || !args[0] || typeof args[0] !== 'object') {
      return null;
    }

    const record = args[0] as Record<string, unknown>;
    const notificationRaw = record['notification'] ?? record['Notification'];
    if (!notificationRaw || typeof notificationRaw !== 'object') {
      return null;
    }

    const excludeRaw = record['excludeUserId'] ?? record['ExcludeUserId'];
    const teamRaw = record['teamId'] ?? record['TeamId'];

    return {
      notification: this.normalizeNotification(notificationRaw),
      teamId: teamRaw == null ? null : Number(teamRaw),
      excludeUserId: excludeRaw == null || excludeRaw === '' ? null : Number(excludeRaw)
    };
  }

  private async invokeJoinTeam(teamId: number): Promise<void> {
    if (!this.connection || this.connection.state !== signalR.HubConnectionState.Connected) {
      return;
    }

    try {
      await this.connection.invoke('JoinTeam', teamId);
    } catch {
      // ignore
    }
  }

  private async invokeRefreshTeamGroups(): Promise<void> {
    if (!this.connection || this.connection.state !== signalR.HubConnectionState.Connected) {
      return;
    }

    try {
      await this.connection.invoke('RefreshTeamGroups');
    } catch {
      // ignore
    }
  }

  private normalizeNotification(raw: unknown): AppNotification {
    const item = (raw ?? {}) as Record<string, unknown>;
    return {
      id: String(item['id'] ?? item['Id'] ?? ''),
      type: String(item['type'] ?? item['Type'] ?? ''),
      title: String(item['title'] ?? item['Title'] ?? ''),
      body: String(item['body'] ?? item['Body'] ?? ''),
      teamId: (item['teamId'] ?? item['TeamId'] ?? null) as number | null,
      boardId: (item['boardId'] ?? item['BoardId'] ?? null) as number | null,
      taskId: (item['taskId'] ?? item['TaskId'] ?? null) as number | null,
      announcementId: (item['announcementId'] ?? item['AnnouncementId'] ?? null) as number | null,
      sprintId: (item['sprintId'] ?? item['SprintId'] ?? null) as number | null,
      isRead: Boolean(item['isRead'] ?? item['IsRead'] ?? false),
      createdAtUtc: String(item['createdAtUtc'] ?? item['CreatedAtUtc'] ?? new Date().toISOString())
    };
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
