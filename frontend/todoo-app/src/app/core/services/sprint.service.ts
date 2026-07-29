import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  BoardKapsam,
  CancelSprintRequest,
  CompleteSprintRequest,
  CreateSprintRequest,
  SprintDetail,
  SprintTask,
  UpdateSprintRequest
} from '../../models/sprint.model';

@Injectable({ providedIn: 'root' })
export class SprintService {
  private readonly http = inject(HttpClient);
  private readonly api = environment.apiUrl;

  getKapsam(teamId: number, boardId: number): Observable<BoardKapsam> {
    return this.http.get<BoardKapsam>(`${this.api}/teams/${teamId}/boards/${boardId}/kapsam`);
  }

  createSprint(teamId: number, boardId: number, request: CreateSprintRequest): Observable<SprintDetail> {
    return this.http.post<SprintDetail>(`${this.api}/teams/${teamId}/boards/${boardId}/sprints`, request);
  }

  updateSprint(sprintId: number, request: UpdateSprintRequest): Observable<SprintDetail> {
    return this.http.put<SprintDetail>(`${this.api}/sprints/${sprintId}`, request);
  }

  startSprint(sprintId: number): Observable<SprintDetail> {
    return this.http.post<SprintDetail>(`${this.api}/sprints/${sprintId}/start`, {});
  }

  completeSprint(sprintId: number, request: CompleteSprintRequest): Observable<SprintDetail> {
    return this.http.post<SprintDetail>(`${this.api}/sprints/${sprintId}/complete`, request);
  }

  cancelSprint(sprintId: number, request: CancelSprintRequest): Observable<SprintDetail> {
    return this.http.post<SprintDetail>(`${this.api}/sprints/${sprintId}/cancel`, request);
  }

  deleteSprint(sprintId: number): Observable<void> {
    return this.http.delete<void>(`${this.api}/sprints/${sprintId}`);
  }

  moveTaskToSprint(sprintId: number, taskId: number, targetIndex?: number): Observable<SprintTask> {
    return this.http.post<SprintTask>(`${this.api}/sprints/${sprintId}/tasks/${taskId}`, {
      targetIndex: targetIndex ?? null
    });
  }

  moveTaskToBacklog(taskId: number, targetIndex?: number): Observable<void> {
    return this.http.post<void>(`${this.api}/taskitems/${taskId}/move-to-backlog`, {
      targetIndex: targetIndex ?? null
    });
  }

  reorderSprintTasks(sprintId: number, taskIds: number[]): Observable<void> {
    return this.http.put<void>(`${this.api}/sprints/${sprintId}/tasks/reorder`, { taskIds });
  }

  reorderBacklog(teamId: number, boardId: number, taskIds: number[]): Observable<void> {
    return this.http.put<void>(`${this.api}/teams/${teamId}/boards/${boardId}/backlog/reorder`, {
      taskIds
    });
  }
}
