import { DatePipe } from '@angular/common';
import { Component, HostListener, input, output } from '@angular/core';
import { TeamAnnouncement } from '../../../../../models/team.model';

@Component({
  selector: 'app-announcement-detail-modal',
  imports: [DatePipe],
  templateUrl: './announcement-detail-modal.html',
  styleUrl: './announcement-detail-modal.scss'
})
export class AnnouncementDetailModalComponent {
  readonly announcement = input.required<TeamAnnouncement>();
  readonly canDelete = input(false);
  readonly canPublishNow = input(false);
  readonly deleting = input(false);
  readonly publishing = input(false);
  readonly statusLabel = input('Yayınlandı');
  readonly statusTone = input('tone-published');

  readonly closed = output<void>();
  readonly deleted = output<TeamAnnouncement>();
  readonly publishNow = output<TeamAnnouncement>();

  private backdropCloseArmed = false;

  constructor() {
    queueMicrotask(() => {
      this.backdropCloseArmed = true;
    });
  }

  @HostListener('document:keydown.escape')
  onEscapeKey(): void {
    this.close();
  }

  close(): void {
    this.closed.emit();
  }

  onBackdropClick(): void {
    if (!this.backdropCloseArmed) {
      return;
    }
    this.close();
  }

  deleteAnnouncement(): void {
    if (!this.canDelete() || this.deleting()) {
      return;
    }
    this.deleted.emit(this.announcement());
  }

  publishAnnouncement(): void {
    if (!this.canPublishNow() || this.publishing()) {
      return;
    }
    this.publishNow.emit(this.announcement());
  }

  toLocalDate(value: string): Date {
    const hasTimezone = /[zZ]|[+-]\d{2}:\d{2}$/.test(value);
    return new Date(hasTimezone ? value : `${value}Z`);
  }
}
