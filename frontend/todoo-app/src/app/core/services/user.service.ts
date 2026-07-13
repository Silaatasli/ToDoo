import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { UpdateProfileRequest, UserProfile, UserSearchResult } from '../../models/user.model';

@Injectable({ providedIn: 'root' })
export class UserService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiUrl}/users`;

  getMyProfile(): Observable<UserProfile> {
    return this.http.get<UserProfile>(`${this.baseUrl}/me`);
  }

  updateMyProfile(request: UpdateProfileRequest): Observable<UserProfile> {
    return this.http.put<UserProfile>(`${this.baseUrl}/me`, request);
  }

  getProfile(id: number): Observable<UserProfile> {
    return this.http.get<UserProfile>(`${this.baseUrl}/${id}`);
  }

  searchUsers(query: string): Observable<UserSearchResult[]> {
    return this.http.get<UserSearchResult[]>(`${this.baseUrl}/search`, {
      params: { q: query }
    });
  }

  uploadMyPhoto(file: File): Observable<UserProfile> {
    const formData = new FormData();
    formData.append('file', file);
    return this.http.post<UserProfile>(`${this.baseUrl}/me/photo`, formData);
  }

  deleteMyPhoto(): Observable<UserProfile> {
    return this.http.delete<UserProfile>(`${this.baseUrl}/me/photo`);
  }

  getPhoto(userId: number): Observable<Blob> {
    return this.http.get(`${this.baseUrl}/${userId}/photo`, {
      responseType: 'blob'
    });
  }

  getMyPhoto(): Observable<Blob> {
    return this.http.get(`${this.baseUrl}/me/photo`, {
      responseType: 'blob'
    });
  }
}
