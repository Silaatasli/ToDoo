import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  TaskAttachment,
  TaskDetail,
  TaskListItem,
  TeamActivityLog,
  UpdateTaskRequest
} from '../../models/team.model';

@Injectable({ providedIn: 'root' })
export class TaskService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiUrl}/taskitems`;

  getTask(taskId: number): Observable<TaskDetail> {
    return this.http.get<TaskDetail>(`${this.baseUrl}/${taskId}`);
  }

  getTaskActivity(taskId: number): Observable<TeamActivityLog[]> {
    return this.http.get<TeamActivityLog[]>(`${this.baseUrl}/${taskId}/activity`);
  }

  updateTask(taskId: number, request: UpdateTaskRequest): Observable<TaskListItem> {
    return this.http.put<TaskListItem>(`${this.baseUrl}/${taskId}`, request);
  }

  moveToColumn(taskId: number, boardColumnId: number): Observable<TaskListItem> {
    return this.http.patch<TaskListItem>(`${this.baseUrl}/${taskId}/column`, { boardColumnId });
  }

  complete(taskId: number): Observable<TaskListItem> {
    return this.http.patch<TaskListItem>(`${this.baseUrl}/${taskId}/complete`, {});
  }

  reopen(taskId: number): Observable<TaskListItem> {
    return this.http.patch<TaskListItem>(`${this.baseUrl}/${taskId}/reopen`, {});
  }

  assign(taskId: number, assignedToUserId: number | null): Observable<TaskListItem> {
    return this.http.patch<TaskListItem>(`${this.baseUrl}/${taskId}/assign`, { assignedToUserId });
  }

  acceptAssignment(taskId: number): Observable<TaskListItem> {
    return this.http.post<TaskListItem>(`${this.baseUrl}/${taskId}/accept-assignment`, {});
  }

  declineAssignment(taskId: number): Observable<TaskListItem> {
    return this.http.post<TaskListItem>(`${this.baseUrl}/${taskId}/decline-assignment`, {});
  }

  delete(taskId: number): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${taskId}`);
  }

  listAttachments(taskId: number): Observable<TaskAttachment[]> {
    return this.http.get<TaskAttachment[]>(`${this.baseUrl}/${taskId}/attachments`);
  }

  uploadAttachment(taskId: number, file: File): Observable<TaskAttachment> {
    const formData = new FormData();
    formData.append('file', file);
    return this.http.post<TaskAttachment>(`${this.baseUrl}/${taskId}/attachments`, formData);
  }

  downloadAttachment(taskId: number, attachmentId: number): Observable<Blob> {
    return this.http.get(`${this.baseUrl}/${taskId}/attachments/${attachmentId}/download`, {
      responseType: 'blob'
    });
  }

  deleteAttachment(taskId: number, attachmentId: number): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${taskId}/attachments/${attachmentId}`);
  }
}
