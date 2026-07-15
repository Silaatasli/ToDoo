import { Injectable, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { AuthService } from './auth.service';
import { RecentItem, RecentItemKind } from '../../models/recent-item.model';

const MAX_ITEMS = 10;

@Injectable({ providedIn: 'root' })
export class RecentItemsService {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  readonly items = signal<RecentItem[]>([]);

  load(): void {
    const storageKey = this.storageKey();
    if (!storageKey) {
      this.items.set([]);
      return;
    }

    try {
      const raw = localStorage.getItem(storageKey);
      if (!raw) {
        this.items.set([]);
        return;
      }

      const parsed = JSON.parse(raw) as RecentItem[];
      this.items.set(Array.isArray(parsed) ? parsed : []);
    } catch {
      this.items.set([]);
    }
  }

  recordTask(input: {
    taskId: number;
    title: string;
    teamId: number;
    teamName: string;
    boardId: number;
    boardName: string;
    boardColumnTitle?: string | null;
  }): void {
    const parts = [input.teamName, input.boardName, input.boardColumnTitle?.trim()]
      .filter((part): part is string => !!part && part.trim().length > 0)
      .map((part) => part.trim());

    this.push({
      key: `task:${input.taskId}`,
      kind: 'task',
      id: input.taskId,
      title: input.title.trim() || 'Görev',
      subtitle: parts.join(' · '),
      visitedAt: Date.now(),
      teamId: input.teamId,
      boardId: input.boardId
    });
  }

  recordBoard(input: {
    boardId: number;
    boardName: string;
    teamId: number;
    teamName: string;
  }): void {
    this.push({
      key: `board:${input.boardId}`,
      kind: 'board',
      id: input.boardId,
      title: input.boardName.trim() || 'Pano',
      subtitle: input.teamName.trim() || 'Takım',
      visitedAt: Date.now(),
      teamId: input.teamId,
      boardId: input.boardId
    });
  }

  recordTeam(input: { teamId: number; teamName: string }): void {
    this.push({
      key: `team:${input.teamId}`,
      kind: 'team',
      id: input.teamId,
      title: input.teamName.trim() || 'Takım',
      subtitle: 'Takım',
      visitedAt: Date.now(),
      teamId: input.teamId
    });
  }

  navigate(item: RecentItem): void {
    if (item.kind === 'task' && item.boardId != null) {
      void this.router.navigate(['/teams', item.teamId, 'boards', item.boardId], {
        queryParams: { taskId: item.id }
      });
      return;
    }

    if (item.kind === 'board') {
      void this.router.navigate(['/teams', item.teamId, 'boards', item.id]);
      return;
    }

    void this.router.navigate(['/teams', item.id, 'board']);
  }

  kindLabel(kind: RecentItemKind): string {
    switch (kind) {
      case 'task':
        return 'Görev';
      case 'board':
        return 'Pano';
      case 'team':
        return 'Takım';
    }
  }

  private push(item: RecentItem): void {
    const storageKey = this.storageKey();
    if (!storageKey) {
      return;
    }

    const next = [item, ...this.items().filter((entry) => entry.key !== item.key)].slice(0, MAX_ITEMS);
    this.items.set(next);
    localStorage.setItem(storageKey, JSON.stringify(next));
  }

  private storageKey(): string | null {
    const userId = this.auth.getUser()?.userId;
    return userId == null ? null : `todoo_recent_${userId}`;
  }
}
