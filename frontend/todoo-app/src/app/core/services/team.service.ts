import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  AddColumnRequest,
  AddMemberRequest,
  BoardColumn,
  CreateTeamRequest,
  CreateTeamTaskRequest,
  ReorderColumnsRequest,
  TaskListItem,
  TeamActivityLog,
  TeamBoard,
  TeamDetail,
  TeamListItem
} from '../../models/team.model';

@Injectable({ providedIn: 'root' })
export class TeamService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiUrl}/teams`;

  getTeams(): Observable<TeamListItem[]> {
    return this.http.get<TeamListItem[]>(this.baseUrl);
  }

  getTeam(id: number): Observable<TeamDetail> {
    return this.http.get<TeamDetail>(`${this.baseUrl}/${id}`);
  }

  createTeam(request: CreateTeamRequest): Observable<TeamDetail> {
    return this.http.post<TeamDetail>(this.baseUrl, request);
  }

  deleteTeam(id: number): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${id}`);
  }

  getBoard(id: number): Observable<TeamBoard> {
    return this.http.get<TeamBoard>(`${this.baseUrl}/${id}/board`);
  }

  addColumn(id: number, request: AddColumnRequest): Observable<BoardColumn> {
    return this.http.post<BoardColumn>(`${this.baseUrl}/${id}/columns`, request);
  }

  updateColumn(id: number, columnId: number, request: AddColumnRequest): Observable<BoardColumn> {
    return this.http.put<BoardColumn>(`${this.baseUrl}/${id}/columns/${columnId}`, request);
  }

  reorderColumns(id: number, request: ReorderColumnsRequest): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/${id}/columns/reorder`, request);
  }

  addMember(id: number, request: AddMemberRequest): Observable<{ success: boolean; message: string }> {
    return this.http.post<{ success: boolean; message: string }>(`${this.baseUrl}/${id}/members`, request);
  }

  removeMember(id: number, memberUserId: number): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${id}/members/${memberUserId}`);
  }

  createTask(id: number, request: CreateTeamTaskRequest): Observable<TaskListItem> {
    return this.http.post<TaskListItem>(`${this.baseUrl}/${id}/tasks`, request);
  }

  getActivity(id: number): Observable<TeamActivityLog[]> {
    return this.http.get<TeamActivityLog[]>(`${this.baseUrl}/${id}/activity`);
  }
}
