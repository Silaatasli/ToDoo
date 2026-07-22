import { Injectable, inject } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { Subject } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AuthService } from './auth.service';

export interface BoardChangedEvent {
  teamId: number;
  boardId?: number | null;
  changeType: string;
  actorUserId?: number | null;
  taskId?: number | null;
  announcementId?: number | null;
}

export interface TaskDragEvent {
  teamId: number;
  taskId: number;
  userId: number;
}

export interface TaskDragStartedEvent extends TaskDragEvent {
  sourceColumnId: number;
}

export interface TaskDragMovedEvent extends TaskDragEvent {
  hoverColumnId: number;
}

@Injectable({ providedIn: 'root' })
export class TeamBoardHubService {
  private readonly auth = inject(AuthService);
  private connection: signalR.HubConnection | null = null;
  private activeTeamId: number | null = null;
  private intentionallyStopped = false;
  private lifecycleBound = false;
  private reconnectTimer: ReturnType<typeof setTimeout> | null = null;
  private readonly boardChangedSubject = new Subject<BoardChangedEvent>();
  private readonly taskDragStartedSubject = new Subject<TaskDragStartedEvent>();
  private readonly taskDragMovedSubject = new Subject<TaskDragMovedEvent>();
  private readonly taskDragEndedSubject = new Subject<TaskDragEvent>();

  readonly boardChanged$ = this.boardChangedSubject.asObservable();
  readonly taskDragStarted$ = this.taskDragStartedSubject.asObservable();
  readonly taskDragMoved$ = this.taskDragMovedSubject.asObservable();
  readonly taskDragEnded$ = this.taskDragEndedSubject.asObservable();

  async connect(teamId: number): Promise<void> {
    if (this.activeTeamId === teamId && this.connection?.state === signalR.HubConnectionState.Connected) {
      return;
    }

    await this.disconnect();

    const token = this.auth.getToken();
    if (!token) {
      return;
    }

    this.intentionallyStopped = false;
    this.activeTeamId = teamId;
    this.bindLifecycleListeners();
    this.connection = new signalR.HubConnectionBuilder()
      .withUrl(environment.hubUrl, {
        accessTokenFactory: () => this.auth.getToken() ?? ''
      })
      .withAutomaticReconnect({
        nextRetryDelayInMilliseconds: (retryContext) => {
          const delays = [0, 2000, 5000, 10000];
          if (retryContext.previousRetryCount < delays.length) {
            return delays[retryContext.previousRetryCount];
          }
          return 30000;
        }
      })
      .build();

    this.connection.serverTimeoutInMilliseconds = 60_000;
    this.connection.keepAliveIntervalInMilliseconds = 15_000;

    this.connection.on('BoardChanged', (payload: BoardChangedEvent) => {
      if (payload.teamId === this.activeTeamId) {
        this.boardChangedSubject.next(payload);
      }
    });

    this.connection.on('TaskDragStarted', (payload: TaskDragStartedEvent) => {
      if (payload.teamId === this.activeTeamId) {
        this.taskDragStartedSubject.next(payload);
      }
    });

    this.connection.on('TaskDragMoved', (payload: TaskDragMovedEvent) => {
      if (payload.teamId === this.activeTeamId) {
        this.taskDragMovedSubject.next(payload);
      }
    });

    this.connection.on('TaskDragEnded', (payload: TaskDragEvent) => {
      if (payload.teamId === this.activeTeamId) {
        this.taskDragEndedSubject.next(payload);
      }
    });

    this.connection.onreconnected(async () => {
      const currentTeamId = this.activeTeamId;
      if (currentTeamId === null || !this.connection) {
        return;
      }
      try {
        await this.connection.invoke('JoinTeam', currentTeamId);
      } catch {
        // ignore; ensureConnected will retry if needed
      }
    });

    this.connection.onclose(() => {
      if (this.intentionallyStopped || this.activeTeamId === null || !this.auth.isLoggedIn()) {
        return;
      }
      this.scheduleManualReconnect();
    });

    try {
      await this.connection.start();
      await this.connection.invoke('JoinTeam', teamId);
    } catch {
      this.connection = null;
      if (!this.intentionallyStopped && this.activeTeamId !== null) {
        this.scheduleManualReconnect();
      }
    }
  }

  async notifyTaskDragStart(teamId: number, taskId: number, sourceColumnId: number): Promise<void> {
    if (this.connection?.state !== signalR.HubConnectionState.Connected) {
      return;
    }

    try {
      await this.connection.invoke('NotifyTaskDragStart', teamId, taskId, sourceColumnId);
    } catch {
      // ignore transient hub errors during drag
    }
  }

  async notifyTaskDragMove(teamId: number, taskId: number, hoverColumnId: number): Promise<void> {
    if (this.connection?.state !== signalR.HubConnectionState.Connected) {
      return;
    }

    try {
      await this.connection.invoke('NotifyTaskDragMove', teamId, taskId, hoverColumnId);
    } catch {
      // ignore transient hub errors during drag
    }
  }

  async notifyTaskDragEnd(teamId: number, taskId: number): Promise<void> {
    if (this.connection?.state !== signalR.HubConnectionState.Connected) {
      return;
    }

    try {
      await this.connection.invoke('NotifyTaskDragEnd', teamId, taskId);
    } catch {
      // ignore transient hub errors during drag
    }
  }

  async disconnect(): Promise<void> {
    this.intentionallyStopped = true;
    this.clearReconnectTimer();
    this.unbindLifecycleListeners();

    const connection = this.connection;
    const teamId = this.activeTeamId;

    this.connection = null;
    this.activeTeamId = null;

    if (!connection) {
      return;
    }

    if (teamId !== null && connection.state === signalR.HubConnectionState.Connected) {
      try {
        await connection.invoke('LeaveTeam', teamId);
      } catch {
        // ignore cleanup errors
      }
    }

    try {
      await connection.stop();
    } catch {
      // ignore cleanup errors
    }
  }

  private scheduleManualReconnect(): void {
    this.clearReconnectTimer();
    const teamId = this.activeTeamId;
    if (teamId === null) {
      return;
    }

    this.reconnectTimer = setTimeout(() => {
      this.reconnectTimer = null;
      void this.connect(teamId);
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

  private async ensureConnected(): Promise<void> {
    const teamId = this.activeTeamId;
    if (this.intentionallyStopped || teamId === null || !this.auth.isLoggedIn()) {
      return;
    }

    const state = this.connection?.state;
    if (state === signalR.HubConnectionState.Connected
      || state === signalR.HubConnectionState.Connecting
      || state === signalR.HubConnectionState.Reconnecting) {
      return;
    }

    this.clearReconnectTimer();
    await this.connect(teamId);
  }
}
