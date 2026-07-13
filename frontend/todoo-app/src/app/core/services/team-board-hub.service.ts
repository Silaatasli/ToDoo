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

    this.activeTeamId = teamId;
    this.connection = new signalR.HubConnectionBuilder()
      .withUrl(environment.hubUrl, {
        accessTokenFactory: () => this.auth.getToken() ?? ''
      })
      .withAutomaticReconnect()
      .build();

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

    await this.connection.start();
    await this.connection.invoke('JoinTeam', teamId);
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
}
