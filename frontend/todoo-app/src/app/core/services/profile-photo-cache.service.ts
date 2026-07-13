import { Injectable, inject, signal } from '@angular/core';
import { UserService } from './user.service';

@Injectable({ providedIn: 'root' })
export class ProfilePhotoCacheService {
  private readonly userService = inject(UserService);
  private readonly urls = signal<Record<number, string>>({});
  private readonly loadingIds = new Set<number>();

  photoUrl(userId: number | null | undefined): string | null {
    if (userId == null) {
      return null;
    }
    return this.urls()[userId] ?? null;
  }

  ensure(userId: number, hasPhoto: boolean): void {
    if (!hasPhoto || this.urls()[userId] || this.loadingIds.has(userId)) {
      return;
    }

    this.loadingIds.add(userId);
    this.userService.getPhoto(userId).subscribe({
      next: (blob) => {
        this.loadingIds.delete(userId);
        if (!blob || blob.size === 0) {
          return;
        }
        this.setBlob(userId, blob);
      },
      error: () => {
        this.loadingIds.delete(userId);
      }
    });
  }

  ensureMany(entries: Array<{ userId: number; hasProfilePhoto: boolean }>): void {
    for (const entry of entries) {
      this.ensure(entry.userId, entry.hasProfilePhoto);
    }
  }

  setBlob(userId: number, blob: Blob): void {
    const previous = this.urls()[userId];
    if (previous) {
      URL.revokeObjectURL(previous);
    }

    const url = URL.createObjectURL(blob);
    this.urls.update((current) => ({ ...current, [userId]: url }));
  }

  clear(userId: number): void {
    const previous = this.urls()[userId];
    if (previous) {
      URL.revokeObjectURL(previous);
    }

    this.urls.update((current) => {
      const next = { ...current };
      delete next[userId];
      return next;
    });
  }
}
