import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  AddColumnRequest,
  AddMemberRequest,
  BoardColumn,
  BoardListItem,
  CreateBoardRequest,
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

  getBoards(teamId: number): Observable<BoardListItem[]> {
    return this.http.get<BoardListItem[]>(`${this.baseUrl}/${teamId}/boards`);
  }

  createBoard(teamId: number, request: CreateBoardRequest): Observable<BoardListItem> {
    return this.http.post<BoardListItem>(`${this.baseUrl}/${teamId}/boards`, request);
  }

  deleteBoard(teamId: number, boardId: number): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${teamId}/boards/${boardId}`);
  }

  getBoard(teamId: number, boardId: number): Observable<TeamBoard> {
    return this.http.get<TeamBoard>(`${this.baseUrl}/${teamId}/boards/${boardId}`);
  }

  /** First board (DisplayOrder) — used for legacy redirects. */
  getDefaultBoard(teamId: number): Observable<TeamBoard> {
    return this.http.get<TeamBoard>(`${this.baseUrl}/${teamId}/board`);
  }

  addColumn(teamId: number, boardId: number, request: AddColumnRequest): Observable<BoardColumn> {
    return this.http.post<BoardColumn>(`${this.baseUrl}/${teamId}/boards/${boardId}/columns`, request);
  }

  updateColumn(
    teamId: number,
    boardId: number,
    columnId: number,
    request: AddColumnRequest
  ): Observable<BoardColumn> {
    return this.http.put<BoardColumn>(
      `${this.baseUrl}/${teamId}/boards/${boardId}/columns/${columnId}`,
      request
    );
  }

  reorderColumns(teamId: number, boardId: number, request: ReorderColumnsRequest): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/${teamId}/boards/${boardId}/columns/reorder`, request);
  }

  addMember(id: number, request: AddMemberRequest): Observable<{ success: boolean; message: string }> {
    return this.http.post<{ success: boolean; message: string }>(`${this.baseUrl}/${id}/members`, request);
  }

  removeMember(id: number, memberUserId: number): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${id}/members/${memberUserId}`);
  }

  createTask(teamId: number, boardId: number, request: CreateTeamTaskRequest): Observable<TaskListItem> {
    return this.http.post<TaskListItem>(`${this.baseUrl}/${teamId}/boards/${boardId}/tasks`, request);
  }

  getActivity(id: number): Observable<TeamActivityLog[]> {
    return this.http.get<TeamActivityLog[]>(`${this.baseUrl}/${id}/activity`);
  }
}
