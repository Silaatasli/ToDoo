import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { AuthService } from '../../../core/services/auth.service';
import { NotificationService } from '../../../core/services/notification.service';
import { TeamBoardHubService } from '../../../core/services/team-board-hub.service';
import { TeamService } from '../../../core/services/team.service';
import {
  AnnouncementPublishMode,
  AnnouncementStatus,
  TeamAnnouncement,
  TeamDetail,
  TeamMember
} from '../../../models/team.model';
import { AppLayout } from '../../../shared/components/app-layout/app-layout';
import { TeamWorkspaceShell } from '../../../shared/components/team-workspace-shell/team-workspace-shell';
import { memberName } from '../team-board/board-ui.utils';
import { AnnouncementDetailModalComponent } from './components/announcement-detail-modal/announcement-detail-modal';

@Component({
  selector: 'app-team-announcements',
  imports: [AppLayout, TeamWorkspaceShell, ReactiveFormsModule, AnnouncementDetailModalComponent],
  templateUrl: './team-announcements.html',
  styleUrl: './team-announcements.scss'
})
export class TeamAnnouncementsPage implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly teamService = inject(TeamService);
  private readonly auth = inject(AuthService);
  private readonly boardHub = inject(TeamBoardHubService);
  private readonly notificationService = inject(NotificationService);
  private readonly fb = inject(FormBuilder);
  private readonly destroyRef = inject(DestroyRef);

  private readonly user = this.auth.getUser();

  readonly teamId = signal<number | null>(null);
  readonly team = signal<TeamDetail | null>(null);
  readonly announcements = signal<TeamAnnouncement[]>([]);
  readonly openAnnouncementId = signal<number | null>(null);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly publishing = signal(false);
  readonly publishError = signal<string | null>(null);
  readonly deletingId = signal<number | null>(null);
  readonly publishingId = signal<number | null>(null);
  readonly permissionBusyUserId = signal<number | null>(null);

  readonly form = this.fb.nonNullable.group({
    title: ['', [Validators.required, Validators.maxLength(200)]],
    body: ['', [Validators.required, Validators.maxLength(4000)]],
    publishMode: ['Now' as AnnouncementPublishMode, [Validators.required]],
    scheduledPublishAt: ['']
  });

  readonly isLeader = computed(() => {
    const team = this.team();
    return !!team && team.leaderUserId === this.user?.userId;
  });

  readonly canPublish = computed(() => {
    const team = this.team();
    if (!team || !this.user) {
      return false;
    }
    if (team.leaderUserId === this.user.userId) {
      return true;
    }
    return team.members.some(
      (member) => member.userId === this.user!.userId && member.canPublishAnnouncements
    );
  });

  readonly permissionMembers = computed(() => {
    const team = this.team();
    if (!team) {
      return [];
    }
    return team.members.filter((member) => member.userId !== team.leaderUserId);
  });

  readonly openAnnouncement = computed(() => {
    const id = this.openAnnouncementId();
    if (id == null) {
      return null;
    }
    return this.announcements().find((item) => item.id === id) ?? null;
  });

  readonly memberName = memberName;
  private schedulePollTimer: ReturnType<typeof setInterval> | null = null;

  ngOnInit(): void {
    this.form.controls.publishMode.valueChanges
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((mode) => {
        if (mode === 'Now' || mode === 'Draft') {
          this.form.controls.scheduledPublishAt.setValue('');
        }
      });

    this.route.paramMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((params) => {
      const id = Number(params.get('id'));
      if (!Number.isFinite(id)) {
        this.error.set('Geçersiz takım.');
        this.loading.set(false);
        return;
      }
      this.teamId.set(id);
      this.load(id);
      void this.boardHub.connect(id);
    });

    this.route.queryParamMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((params) => {
      const raw = params.get('announcementId');
      const id = raw ? Number(raw) : null;
      this.openAnnouncementId.set(id != null && Number.isFinite(id) ? id : null);
    });

    this.boardHub.boardChanged$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((event) => {
      if (event.teamId !== this.teamId()) {
        return;
      }

      if (
        event.changeType === 'announcement-published' ||
        event.changeType === 'announcement-changed'
      ) {
        const id = this.teamId();
        if (id != null) {
          this.refreshAnnouncements(id);
        }
        // Liste/popup SignalR bildirim hub'indan gelir; yedek olarak listeyi tazele.
        this.notificationService.load();
      }
    });

    this.destroyRef.onDestroy(() => {
      this.stopSchedulePolling();
      void this.boardHub.disconnect();
    });
  }

  openAnnouncementDetail(item: TeamAnnouncement): void {
    queueMicrotask(() => {
      this.openAnnouncementId.set(item.id);
      void this.router.navigate([], {
        relativeTo: this.route,
        queryParams: { announcementId: item.id },
        queryParamsHandling: 'merge'
      });
    });
  }

  closeAnnouncementDetail(): void {
    this.openAnnouncementId.set(null);
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { announcementId: null },
      queryParamsHandling: 'merge'
    });
  }

  previewBody(body: string): string {
    const trimmed = body.trim();
    if (trimmed.length <= 140) {
      return trimmed;
    }
    return `${trimmed.slice(0, 140).trimEnd()}…`;
  }

  statusLabel(status: AnnouncementStatus): string {
    switch (this.normalizeStatus(status)) {
      case 'Draft':
        return 'Taslak';
      case 'Scheduled':
        return 'Zamanlanmış';
      default:
        return 'Yayınlandı';
    }
  }

  statusTone(status: AnnouncementStatus): string {
    switch (this.normalizeStatus(status)) {
      case 'Draft':
        return 'tone-draft';
      case 'Scheduled':
        return 'tone-scheduled';
      default:
        return 'tone-published';
    }
  }

  normalizeStatus(status: AnnouncementStatus): 'Draft' | 'Scheduled' | 'Published' {
    if (status === 0 || status === 'Draft') {
      return 'Draft';
    }
    if (status === 1 || status === 'Scheduled') {
      return 'Scheduled';
    }
    return 'Published';
  }

  canPublishNow(item: TeamAnnouncement): boolean {
    const status = this.normalizeStatus(item.status);
    return this.canPublish() && (status === 'Draft' || status === 'Scheduled');
  }

  listMeta(item: TeamAnnouncement): string {
    const author = item.authorDisplayName || item.authorEmail;
    const status = this.normalizeStatus(item.status);
    if (status === 'Published' && item.publishedAt) {
      return `${author} · Yayın: ${this.formatDateTime(item.publishedAt)}`;
    }
    if (status === 'Scheduled' && item.scheduledPublishAt) {
      return `${author} · Plan: ${this.formatDateTime(item.scheduledPublishAt)}`;
    }
    return `${author} · Oluşturulma: ${this.formatDateTime(item.createdDate)}`;
  }

  submitAnnouncement(): void {
    const id = this.teamId();
    if (!id || this.form.invalid || this.publishing()) {
      this.form.markAllAsTouched();
      return;
    }

    const { title, body, publishMode, scheduledPublishAt } = this.form.getRawValue();
    if (publishMode === 'Schedule' && !scheduledPublishAt) {
      this.publishError.set('Zamanlanmış duyuru için yayın tarihi seçin.');
      return;
    }

    this.publishing.set(true);
    this.publishError.set(null);

    this.teamService
      .createAnnouncement(id, {
        title: title.trim(),
        body: body.trim(),
        publishMode,
        scheduledPublishAt:
          publishMode === 'Schedule' && scheduledPublishAt
            ? this.toUtcIsoFromLocalInput(scheduledPublishAt)
            : null
      })
      .subscribe({
        next: (created) => {
          this.publishing.set(false);
          this.form.reset({
            title: '',
            body: '',
            publishMode: 'Now',
            scheduledPublishAt: ''
          });
          this.announcements.update((items) => [created, ...items]);
          this.syncSchedulePolling();
          if (this.normalizeStatus(created.status) === 'Published') {
            this.openAnnouncementDetail(created);
          }
        },
        error: (err: HttpErrorResponse) => {
          this.publishing.set(false);
          this.publishError.set(err.error?.message ?? 'Duyuru kaydedilemedi.');
        }
      });
  }

  publishNow(item: TeamAnnouncement): void {
    const id = this.teamId();
    if (!id || this.publishingId() === item.id) {
      return;
    }

    this.publishingId.set(item.id);
    this.teamService.publishAnnouncement(id, item.id).subscribe({
      next: (published) => {
        this.publishingId.set(null);
        this.announcements.update((items) =>
          items.map((entry) => (entry.id === published.id ? published : entry))
        );
      },
      error: (err: HttpErrorResponse) => {
        this.publishingId.set(null);
        this.error.set(err.error?.message ?? 'Duyuru yayınlanamadı.');
      }
    });
  }

  deleteAnnouncement(item: TeamAnnouncement, event?: Event): void {
    event?.stopPropagation();
    const id = this.teamId();
    if (!id || !confirm('Bu duyuru silinsin mi?')) {
      return;
    }

    this.deletingId.set(item.id);
    this.teamService.deleteAnnouncement(id, item.id).subscribe({
      next: () => {
        this.deletingId.set(null);
        this.announcements.update((items) => items.filter((entry) => entry.id !== item.id));
        if (this.openAnnouncementId() === item.id) {
          this.closeAnnouncementDetail();
        }
      },
      error: (err: HttpErrorResponse) => {
        this.deletingId.set(null);
        this.error.set(err.error?.message ?? 'Duyuru silinemedi.');
      }
    });
  }

  canDelete(item: TeamAnnouncement): boolean {
    return this.isLeader() || item.authorUserId === this.user?.userId;
  }

  togglePublishPermission(member: TeamMember): void {
    const id = this.teamId();
    if (!id || !this.isLeader() || this.permissionBusyUserId() === member.userId) {
      return;
    }

    const next = !member.canPublishAnnouncements;
    this.permissionBusyUserId.set(member.userId);

    this.teamService.setAnnouncementPublishPermission(id, member.userId, next).subscribe({
      next: () => {
        this.permissionBusyUserId.set(null);
        this.team.update((current) => {
          if (!current) {
            return current;
          }
          return {
            ...current,
            members: current.members.map((entry) =>
              entry.userId === member.userId ? { ...entry, canPublishAnnouncements: next } : entry
            )
          };
        });
      },
      error: (err: HttpErrorResponse) => {
        this.permissionBusyUserId.set(null);
        this.error.set(err.error?.message ?? 'Yetki güncellenemedi.');
      }
    });
  }

  toLocalDate(value: string): Date {
    const hasTimezone = /[zZ]|[+-]\d{2}:\d{2}$/.test(value);
    return new Date(hasTimezone ? value : `${value}Z`);
  }

  private formatDateTime(value: string): string {
    return this.toLocalDate(value).toLocaleString('tr-TR', {
      day: '2-digit',
      month: 'short',
      year: 'numeric',
      hour: '2-digit',
      minute: '2-digit'
    });
  }

  /** datetime-local value (YYYY-MM-DDTHH:mm) -> UTC ISO, avoiding ambiguous parsing. */
  private toUtcIsoFromLocalInput(localValue: string): string {
    const match = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})(?::(\d{2}))?/.exec(localValue.trim());
    if (!match) {
      return new Date(localValue).toISOString();
    }

    const year = Number(match[1]);
    const month = Number(match[2]) - 1;
    const day = Number(match[3]);
    const hour = Number(match[4]);
    const minute = Number(match[5]);
    const second = Number(match[6] ?? '0');
    return new Date(year, month, day, hour, minute, second).toISOString();
  }

  private syncSchedulePolling(): void {
    const hasScheduled = this.announcements().some(
      (item) => this.normalizeStatus(item.status) === 'Scheduled'
    );

    if (hasScheduled) {
      this.startSchedulePolling();
      return;
    }

    this.stopSchedulePolling();
  }

  private startSchedulePolling(): void {
    if (this.schedulePollTimer) {
      return;
    }

    this.schedulePollTimer = setInterval(() => {
      const id = this.teamId();
      if (id == null) {
        return;
      }
      this.refreshAnnouncements(id);
    }, 3000);
  }

  private stopSchedulePolling(): void {
    if (!this.schedulePollTimer) {
      return;
    }
    clearInterval(this.schedulePollTimer);
    this.schedulePollTimer = null;
  }

  private refreshAnnouncements(teamId: number): void {
    this.teamService.getAnnouncements(teamId).subscribe({
      next: (items) => {
        this.announcements.set(items);
        this.syncSchedulePolling();
      },
      error: () => {
        // Keep current list; next poll can retry.
      }
    });
  }

  private load(teamId: number): void {
    this.loading.set(true);
    this.error.set(null);

    this.teamService.getTeam(teamId).subscribe({
      next: (team) => {
        this.team.set(team);
        this.teamService.getAnnouncements(teamId).subscribe({
          next: (items) => {
            this.announcements.set(items);
            this.loading.set(false);
            this.syncSchedulePolling();

            const openId = this.openAnnouncementId();
            if (openId != null && !items.some((item) => item.id === openId)) {
              this.closeAnnouncementDetail();
            }
          },
          error: (err: HttpErrorResponse) => {
            this.loading.set(false);
            this.error.set(err.error?.message ?? 'Duyurular yüklenemedi.');
          }
        });
      },
      error: (err: HttpErrorResponse) => {
        this.loading.set(false);
        this.error.set(err.error?.message ?? 'Takım yüklenemedi.');
      }
    });
  }
}
