import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { GlobalSearchResult } from '../../models/search.model';

@Injectable({ providedIn: 'root' })
export class SearchService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiUrl}/search`;

  search(query: string): Observable<GlobalSearchResult> {
    return this.http.get<GlobalSearchResult>(this.baseUrl, {
      params: { q: query }
    });
  }
}
