import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { TaskReport } from '../../models/report.model';

@Injectable({ providedIn: 'root' })
export class ReportService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiUrl}/reports`;

  getTaskSummary(teamId: number): Observable<TaskReport> {
    const params = new HttpParams().set('teamId', teamId);
    return this.http.get<TaskReport>(`${this.baseUrl}/task-summary`, { params });
  }
}
