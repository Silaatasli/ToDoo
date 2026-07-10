import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, DestroyRef, HostListener, inject, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { catchError, debounceTime, distinctUntilChanged, EMPTY, of, switchMap } from 'rxjs';
import { AuthService } from '../../../core/services/auth.service';
import { CategoryService } from '../../../core/services/category.service';
import { TeamBoardHubService } from '../../../core/services/team-board-hub.service';
import { TaskService } from '../../../core/services/task.service';
import { TeamService } from '../../../core/services/team.service';
import { UserService } from '../../../core/services/user.service';
import { AppLayout } from '../../../shared/components/app-layout/app-layout';
import { Category } from '../../../models/category.model';
import {
  AssignmentStatus,
  BoardColumnWithTasks,
  Priority,
  TaskActivityAction,
  TaskAttachment,
  TaskDetail,
  TaskListItem,
  TeamActivityLog,
  TeamBoard as TeamBoardModel,
  TeamDetail,
  TeamMember
} from '../../../models/team.model';
import { UserSearchResult } from '../../../models/user.model';

interface RemoteTaskDrag {
  userId: number;
  taskId: number;
  sourceColumnId: number;
  hoverColumnId: number;
}

@Component({
  selector: 'app-team-board',
  imports: [AppLayout, ReactiveFormsModule, DatePipe, RouterLink],
  templateUrl: './team-board.html',
  styleUrl: './team-board.scss'
})
export class TeamBoard implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly teamService = inject(TeamService);
  private readonly taskService = inject(TaskService);
  private readonly userService = inject(UserService);
  private readonly categoryService = inject(CategoryService);
  private readonly auth = inject(AuthService);
  private readonly boardHub = inject(TeamBoardHubService);
  private readonly fb = inject(FormBuilder);
  private readonly destroyRef = inject(DestroyRef);
  private readonly sanitizer = inject(DomSanitizer);

  readonly teamId = signal<number | null>(null);
  readonly board = signal<TeamBoardModel | null>(null);
  readonly team = signal<TeamDetail | null>(null);
  readonly categories = signal<Category[]>([]);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

  readonly showColumnModal = signal(false);
  readonly savingColumn = signal(false);
  readonly columnError = signal<string | null>(null);

  readonly editingColumnId = signal<number | null>(null);
  readonly savingColumnEdit = signal(false);
  readonly editColumnError = signal<string | null>(null);

  readonly showTaskModal = signal(false);
  readonly savingTask = signal(false);
  readonly taskError = signal<string | null>(null);
  readonly targetColumnId = signal<number | null>(null);

  readonly showMembersModal = signal(false);
  readonly savingMember = signal(false);
  readonly memberError = signal<string | null>(null);
  readonly memberSearchResults = signal<UserSearchResult[]>([]);
  readonly memberSearchLoading = signal(false);
  readonly showMemberSearchResults = signal(false);

  readonly showNewCategory = signal(false);
  readonly creatingCategory = signal(false);
  readonly newCategoryError = signal<string | null>(null);
  readonly newCategoryControl = this.fb.nonNullable.control('', [Validators.maxLength(100)]);

  readonly showDetailModal = signal(false);
  readonly detailLoading = signal(false);
  readonly detail = signal<TaskDetail | null>(null);
  readonly detailError = signal<string | null>(null);
  readonly detailEditing = signal(false);
  readonly savingDetail = signal(false);
  readonly detailSaveError = signal<string | null>(null);
  readonly assigning = signal(false);
  readonly taskActivity = signal<TeamActivityLog[]>([]);
  readonly taskActivityLoading = signal(false);
  readonly taskAttachments = signal<TaskAttachment[]>([]);
  readonly taskAttachmentsLoading = signal(false);
  readonly uploadingAttachment = signal(false);
  readonly attachmentError = signal<string | null>(null);
  readonly deletingAttachmentId = signal<number | null>(null);
  readonly attachmentPreviewUrls = signal<Record<number, string>>({});
  readonly trustedPreviewUrls = signal<Record<number, SafeResourceUrl>>({});
  readonly selectedAttachmentId = signal<number | null>(null);
  readonly attachmentLightbox = signal<TaskAttachment | null>(null);
  readonly attachmentLightboxLoading = signal(false);

  readonly selectedAttachment = computed(() => {
    const id = this.selectedAttachmentId();
    if (id == null) {
      return null;
    }
    return this.taskAttachments().find((attachment) => attachment.id === id) ?? null;
  });

  readonly deletingTeam = signal(false);
  readonly draggedTaskId = signal<number | null>(null);
  readonly dragOverColumnId = signal<number | null>(null);
  readonly remoteTaskDrags = signal<RemoteTaskDrag[]>([]);
  private dragOriginColumnId: number | null = null;
  private lastBroadcastHoverColumnId: number | null = null;
  readonly draggedColumnId = signal<number | null>(null);
  readonly dragOverReorderColumnId = signal<number | null>(null);
  readonly assigneeFilter = signal<'all' | 'unassigned' | number>('all');
  readonly showAssigneeFilterMenu = signal(false);
  readonly showPendingOnlyFilter = signal(false);
  readonly respondingToAssignment = signal(false);

  private readonly user = this.auth.getUser();

  readonly assigneeFilterMember = computed(() => {
    const filter = this.assigneeFilter();
    if (typeof filter !== 'number') {
      return null;
    }

    return this.team()?.members.find((member) => member.userId === filter) ?? null;
  });

  readonly filteredBoard = computed(() => {
    const current = this.board();
    const filter = this.assigneeFilter();
    const pendingOnly = this.showPendingOnlyFilter();
    if (!current || filter === 'all') {
      return current;
    }

    return {
      ...current,
      columns: current.columns.map((column) => ({
        ...column,
        tasks: column.tasks.filter((task) => {
          if (filter === 'unassigned') {
            if (task.assignedToUserId != null) {
              return false;
            }
          } else if (task.assignedToUserId !== filter) {
            return false;
          }

          if (pendingOnly && task.assignmentStatus !== AssignmentStatus.Pending) {
            return false;
          }

          return true;
        })
      }))
    };
  });

  readonly filteredPendingTaskCount = computed(() => {
    const current = this.board();
    const filter = this.assigneeFilter();
    if (!current || filter === 'all' || filter === 'unassigned') {
      return 0;
    }

    return current.columns
      .flatMap((column) => column.tasks)
      .filter(
        (task) =>
          task.assignedToUserId === filter && task.assignmentStatus === AssignmentStatus.Pending
      ).length;
  });

  readonly filteredTaskCount = computed(() => {
    const current = this.filteredBoard();
    if (!current) {
      return 0;
    }

    return current.columns.reduce((total, column) => total + column.tasks.length, 0);
  });

  readonly assignmentStatus = AssignmentStatus;

  readonly isLeader = computed(() => {
    const t = this.team();
    return !!t && t.leaderUserId === this.user?.userId;
  });

  readonly priorityOptions = [
    { value: Priority.Low, label: 'Düşük' },
    { value: Priority.Medium, label: 'Orta' },
    { value: Priority.High, label: 'Yüksek' },
    { value: Priority.Critical, label: 'Kritik' }
  ];

  readonly columnForm = this.fb.nonNullable.group({
    title: ['', [Validators.required, Validators.maxLength(100)]]
  });

  readonly editColumnForm = this.fb.nonNullable.group({
    title: ['', [Validators.required, Validators.maxLength(100)]]
  });

  readonly taskForm = this.fb.nonNullable.group({
    title: ['', [Validators.required, Validators.maxLength(200)]],
    description: [''],
    priority: [Priority.Medium, [Validators.required]],
    categoryId: [''],
    dueDate: [''],
    assignedToUserId: ['']
  });

  readonly memberForm = this.fb.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    searchQuery: ['']
  });

  readonly detailForm = this.fb.nonNullable.group({
    title: ['', [Validators.required, Validators.maxLength(200)]],
    description: [''],
    priority: [Priority.Medium, [Validators.required]],
    categoryId: [''],
    startDate: ['', [Validators.required]],
    dueDate: ['']
  });

  ngOnInit(): void {
    this.categoryService.getAll().subscribe({
      next: (categories) => this.categories.set(categories),
      error: () => {}
    });

    this.route.paramMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((params) => {
      const id = Number(params.get('id'));
      if (Number.isNaN(id)) {
        this.teamId.set(null);
        this.error.set('Geçersiz takım.');
        this.loading.set(false);
        return;
      }

      this.resetModals();
      this.teamId.set(id);
      this.load();
      void this.connectBoardHub(id);
    });

    this.boardHub.boardChanged$
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((event) => {
        const openDetailId = this.detail()?.id;
        const affectsOpenDetail =
          openDetailId !== undefined &&
          (event.taskId == null || event.taskId === openDetailId);

        if (event.actorUserId !== this.user?.userId) {
          this.remoteTaskDrags.set([]);
          this.load();
          if (openDetailId) {
            this.refreshDetail(openDetailId);
          }
        }

        if (this.showDetailModal() && openDetailId && affectsOpenDetail) {
          this.loadTaskActivity(openDetailId);
          this.loadTaskAttachments(openDetailId);
        }
      });

    this.boardHub.taskDragStarted$
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((event) => {
        if (event.userId === this.user?.userId) {
          return;
        }
        this.upsertRemoteDrag({
          userId: event.userId,
          taskId: event.taskId,
          sourceColumnId: event.sourceColumnId,
          hoverColumnId: event.sourceColumnId
        });
      });

    this.boardHub.taskDragMoved$
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((event) => {
        if (event.userId === this.user?.userId) {
          return;
        }
        this.upsertRemoteDrag({
          userId: event.userId,
          taskId: event.taskId,
          sourceColumnId: this.findRemoteDrag(event.userId)?.sourceColumnId ?? event.hoverColumnId,
          hoverColumnId: event.hoverColumnId
        });
      });

    this.boardHub.taskDragEnded$
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((event) => {
        if (event.userId === this.user?.userId) {
          return;
        }
        this.removeRemoteDrag(event.userId);
      });

    this.destroyRef.onDestroy(() => {
      void this.boardHub.disconnect();
    });

    this.memberForm.controls.searchQuery.valueChanges
      .pipe(
        debounceTime(300),
        distinctUntilChanged(),
        switchMap((query) => {
          const trimmed = query.trim();
          if (trimmed.length < 3) {
            this.memberSearchResults.set([]);
            this.memberSearchLoading.set(false);
            this.showMemberSearchResults.set(false);
            return EMPTY;
          }

          this.showMemberSearchResults.set(true);
          this.memberSearchLoading.set(true);
          return this.userService.searchUsers(trimmed).pipe(
            catchError(() => of<UserSearchResult[]>([]))
          );
        }),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe((results) => {
        this.memberSearchLoading.set(false);
        const existingMemberIds = new Set((this.team()?.members ?? []).map((member) => member.userId));
        this.memberSearchResults.set(results.filter((user) => !existingMemberIds.has(user.id)));
      });
  }

  @HostListener('document:keydown.escape')
  onEscapeKey(): void {
    if (this.attachmentLightbox()) {
      this.closeAttachmentLightbox();
    }
  }

  @HostListener('document:click')
  onDocumentClick(): void {
    if (this.showAssigneeFilterMenu()) {
      this.closeAssigneeFilterMenu();
    }

    if (this.showMemberSearchResults()) {
      this.closeMemberSearchResults();
    }
  }

  private async connectBoardHub(teamId: number): Promise<void> {
    try {
      await this.boardHub.connect(teamId);
    } catch {
      // Hub connection is optional; board still works via HTTP refresh.
    }
  }

  private resetModals(): void {
    this.showColumnModal.set(false);
    this.showTaskModal.set(false);
    this.showMembersModal.set(false);
    this.showDetailModal.set(false);
    this.detail.set(null);
    this.taskActivity.set([]);
    this.taskAttachments.set([]);
    this.selectedAttachmentId.set(null);
    this.revokeAttachmentPreviewUrls();
    this.closeAttachmentLightbox();
    this.board.set(null);
    this.team.set(null);
    this.remoteTaskDrags.set([]);
    this.assigneeFilter.set('all');
    this.showPendingOnlyFilter.set(false);
    this.showAssigneeFilterMenu.set(false);
  }

  toggleAssigneeFilterMenu(): void {
    this.showAssigneeFilterMenu.update((open) => !open);
  }

  closeAssigneeFilterMenu(): void {
    this.showAssigneeFilterMenu.set(false);
  }

  selectAssigneeFilter(filter: 'all' | 'unassigned' | number): void {
    this.assigneeFilter.set(filter);
    this.showPendingOnlyFilter.set(false);
    this.showAssigneeFilterMenu.set(false);
  }

  toggleAssigneeFilter(userId: number): void {
    this.assigneeFilter.update((current) => (current === userId ? 'all' : userId));
    this.showPendingOnlyFilter.set(false);
    this.showAssigneeFilterMenu.set(false);
  }

  toggleUnassignedFilter(): void {
    this.assigneeFilter.update((current) => (current === 'unassigned' ? 'all' : 'unassigned'));
    this.showPendingOnlyFilter.set(false);
    this.showAssigneeFilterMenu.set(false);
  }

  clearAssigneeFilter(): void {
    this.assigneeFilter.set('all');
    this.showPendingOnlyFilter.set(false);
    this.showAssigneeFilterMenu.set(false);
  }

  togglePendingOnlyFilter(): void {
    this.showPendingOnlyFilter.update((current) => !current);
  }

  isAssignmentPending(task: TaskListItem): boolean {
    return task.assignmentStatus === AssignmentStatus.Pending;
  }

  canRespondToAssignment(detail: TaskDetail): boolean {
    return (
      detail.assignedToUserId === this.user?.userId &&
      detail.assignmentStatus === AssignmentStatus.Pending
    );
  }

  acceptAssignment(detail: TaskDetail): void {
    if (!this.canRespondToAssignment(detail) || this.respondingToAssignment()) {
      return;
    }

    this.respondingToAssignment.set(true);
    this.taskService.acceptAssignment(detail.id).subscribe({
      next: () => {
        this.respondingToAssignment.set(false);
        this.load();
        this.refreshDetail(detail.id);
        this.loadTaskActivity(detail.id);
      },
      error: (err: HttpErrorResponse) => {
        this.respondingToAssignment.set(false);
        this.detailError.set(err.error?.message ?? 'Görev onaylanamadı.');
      }
    });
  }

  declineAssignment(detail: TaskDetail): void {
    if (!this.canRespondToAssignment(detail) || this.respondingToAssignment()) {
      return;
    }

    if (!confirm('Bu görev atamasını reddetmek istiyor musunuz?')) {
      return;
    }

    this.respondingToAssignment.set(true);
    this.taskService.declineAssignment(detail.id).subscribe({
      next: () => {
        this.respondingToAssignment.set(false);
        this.load();
        this.refreshDetail(detail.id);
        this.loadTaskActivity(detail.id);
      },
      error: (err: HttpErrorResponse) => {
        this.respondingToAssignment.set(false);
        this.detailError.set(err.error?.message ?? 'Görev reddedilemedi.');
      }
    });
  }

  isAssigneeFilterActive(userId: number): boolean {
    return this.assigneeFilter() === userId;
  }

  isUnassignedFilterActive(): boolean {
    return this.assigneeFilter() === 'unassigned';
  }

  load(): void {
    const id = this.teamId();
    if (id === null) {
      return;
    }

    this.loading.set(true);
    this.error.set(null);

    this.teamService.getBoard(id).subscribe({
      next: (board) => {
        this.board.set(board);
        this.loading.set(false);
      },
      error: (err: HttpErrorResponse) => {
        this.loading.set(false);
        this.error.set(
          err.status === 0
            ? "API'ye bağlanılamadı. Backend çalışıyor mu?"
            : (err.error?.message ?? 'Pano yüklenemedi.')
        );
      }
    });

    this.teamService.getTeam(id).subscribe({
      next: (team) => this.team.set(team),
      error: () => this.team.set(null)
    });
  }

  private loadTaskActivity(taskId: number): void {
    this.taskActivityLoading.set(true);
    this.taskService.getTaskActivity(taskId).subscribe({
      next: (logs) => {
        this.taskActivity.set(logs);
        this.taskActivityLoading.set(false);
      },
      error: () => {
        this.taskActivity.set([]);
        this.taskActivityLoading.set(false);
      }
    });
  }

  private loadTaskAttachments(taskId: number): void {
    this.taskAttachmentsLoading.set(true);
    this.attachmentError.set(null);
    this.taskService.listAttachments(taskId).subscribe({
      next: (attachments) => {
        this.taskAttachments.set(attachments);
        this.taskAttachmentsLoading.set(false);
        this.syncSelectedAttachment(attachments);
        this.loadAttachmentPreviews(taskId, attachments);
      },
      error: (err: HttpErrorResponse) => {
        this.taskAttachments.set([]);
        this.taskAttachmentsLoading.set(false);
        this.attachmentError.set(err.error?.message ?? 'Ekler yüklenemedi.');
      }
    });
  }

  private loadAttachmentPreviews(taskId: number, attachments: TaskAttachment[]): void {
    this.revokeAttachmentPreviewUrls();
    const previewableAttachments = attachments.filter(
      (attachment) => this.isImageAttachment(attachment) || this.isPdfAttachment(attachment)
    );

    for (const attachment of previewableAttachments) {
      this.taskService.downloadAttachment(taskId, attachment.id).subscribe({
        next: (blob) => this.registerPreviewBlob(attachment, blob),
        error: () => {}
      });
    }
  }

  private registerPreviewBlob(attachment: TaskAttachment, blob: Blob): void {
    const contentType = this.resolvePreviewContentType(attachment, blob);
    const typedBlob = blob.type === contentType ? blob : new Blob([blob], { type: contentType });
    const url = URL.createObjectURL(typedBlob);

    this.attachmentPreviewUrls.update((current) => ({ ...current, [attachment.id]: url }));
    this.trustedPreviewUrls.update((current) => ({
      ...current,
      [attachment.id]: this.sanitizer.bypassSecurityTrustResourceUrl(url)
    }));
  }

  private resolvePreviewContentType(attachment: TaskAttachment, blob: Blob): string {
    if (this.isPdfAttachment(attachment)) {
      return 'application/pdf';
    }

    if (this.isImageAttachment(attachment)) {
      return blob.type || attachment.contentType || 'image/jpeg';
    }

    return blob.type || attachment.contentType || 'application/octet-stream';
  }

  private syncSelectedAttachment(attachments: TaskAttachment[]): void {
    const currentId = this.selectedAttachmentId();
    if (currentId != null && attachments.some((attachment) => attachment.id === currentId)) {
      return;
    }

    this.selectedAttachmentId.set(attachments[0]?.id ?? null);
  }

  selectAttachment(attachment: TaskAttachment): void {
    this.selectedAttachmentId.set(attachment.id);
  }

  attachmentPreviewUrl(attachmentId: number): string | null {
    return this.attachmentPreviewUrls()[attachmentId] ?? null;
  }

  trustedAttachmentPreviewUrl(attachmentId: number): SafeResourceUrl | null {
    return this.trustedPreviewUrls()[attachmentId] ?? null;
  }

  lightboxTrustedPreviewUrl(): SafeResourceUrl | null {
    const attachment = this.attachmentLightbox();
    if (!attachment) {
      return null;
    }
    return this.trustedAttachmentPreviewUrl(attachment.id);
  }

  private revokeAttachmentPreviewUrls(): void {
    for (const url of Object.values(this.attachmentPreviewUrls())) {
      URL.revokeObjectURL(url);
    }
    this.attachmentPreviewUrls.set({});
    this.trustedPreviewUrls.set({});
  }

  onAttachmentSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    const detail = this.detail();
    if (!file || !detail) {
      return;
    }

    this.uploadingAttachment.set(true);
    this.attachmentError.set(null);

    this.taskService.uploadAttachment(detail.id, file).subscribe({
      next: () => {
        this.uploadingAttachment.set(false);
        this.loadTaskAttachments(detail.id);
        this.loadTaskActivity(detail.id);
      },
      error: (err: HttpErrorResponse) => {
        this.uploadingAttachment.set(false);
        this.attachmentError.set(err.error?.message ?? 'Dosya yüklenemedi.');
      }
    });
  }

  openAttachmentFile(attachment: TaskAttachment): void {
    this.attachmentLightbox.set(attachment);

    const existingUrl = this.attachmentPreviewUrl(attachment.id);
    if (existingUrl) {
      this.attachmentLightboxLoading.set(false);
      return;
    }

    const detail = this.detail();
    if (!detail) {
      return;
    }

    this.attachmentLightboxLoading.set(true);
    this.taskService.downloadAttachment(detail.id, attachment.id).subscribe({
      next: (blob) => {
        this.registerPreviewBlob(attachment, blob);
        this.attachmentLightboxLoading.set(false);
      },
      error: (err: HttpErrorResponse) => {
        this.attachmentLightboxLoading.set(false);
        this.attachmentLightbox.set(null);
        this.attachmentError.set(err.error?.message ?? 'Dosya açılamadı.');
      }
    });
  }

  closeAttachmentLightbox(): void {
    this.attachmentLightbox.set(null);
    this.attachmentLightboxLoading.set(false);
  }

  lightboxPreviewUrl(): string | null {
    const attachment = this.attachmentLightbox();
    if (!attachment) {
      return null;
    }
    return this.attachmentPreviewUrl(attachment.id);
  }

  downloadAttachmentFile(attachment: TaskAttachment): void {
    const detail = this.detail();
    if (!detail) {
      return;
    }

    this.taskService.downloadAttachment(detail.id, attachment.id).subscribe({
      next: (blob) => {
        const url = URL.createObjectURL(blob);
        const anchor = document.createElement('a');
        anchor.href = url;
        anchor.download = attachment.fileName;
        anchor.click();
        URL.revokeObjectURL(url);
      },
      error: (err: HttpErrorResponse) => {
        this.attachmentError.set(err.error?.message ?? 'Dosya indirilemedi.');
      }
    });
  }

  deleteAttachmentFile(attachment: TaskAttachment): void {
    const detail = this.detail();
    if (!detail || !confirm(`"${attachment.fileName}" dosyası silinsin mi?`)) {
      return;
    }

    this.deletingAttachmentId.set(attachment.id);
    this.attachmentError.set(null);

    this.taskService.deleteAttachment(detail.id, attachment.id).subscribe({
      next: () => {
        this.deletingAttachmentId.set(null);
        this.loadTaskAttachments(detail.id);
        this.loadTaskActivity(detail.id);
      },
      error: (err: HttpErrorResponse) => {
        this.deletingAttachmentId.set(null);
        this.attachmentError.set(err.error?.message ?? 'Dosya silinemedi.');
      }
    });
  }

  canDeleteAttachment(attachment: TaskAttachment): boolean {
    const detail = this.detail();
    const team = this.team();
    const userId = this.user?.userId;
    if (!detail || userId == null) {
      return false;
    }

    return (
      attachment.uploadedByUserId === userId ||
      detail.createdByUserId === userId ||
      team?.leaderUserId === userId
    );
  }

  isImageAttachment(attachment: TaskAttachment): boolean {
    return attachment.contentType.startsWith('image/');
  }

  isPdfAttachment(attachment: TaskAttachment): boolean {
    return (
      attachment.contentType === 'application/pdf' ||
      attachment.fileName.toLowerCase().endsWith('.pdf')
    );
  }

  attachmentKindLabel(attachment: TaskAttachment): string {
    if (this.isImageAttachment(attachment)) {
      return 'Görsel';
    }
    if (this.isPdfAttachment(attachment)) {
      return 'PDF';
    }
    return 'Dosya';
  }

  formatFileSize(bytes: number): string {
    if (bytes < 1024) {
      return `${bytes} B`;
    }
    if (bytes < 1024 * 1024) {
      return `${(bytes / 1024).toFixed(1)} KB`;
    }
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  }

  toLocalDate(value: string): Date {
    const hasTimezone = /[zZ]|[+-]\d{2}:\d{2}$/.test(value);
    return new Date(hasTimezone ? value : `${value}Z`);
  }

  private logUserName(log: TeamActivityLog): string {
    const member = this.team()?.members.find((m) => m.userId === log.userId);
    if (member) {
      return this.memberName(member);
    }
    return log.userEmail || 'Bir kullanıcı';
  }

  activityText(log: TeamActivityLog): string {
    const who = this.logUserName(log);
    switch (log.actionType) {
      case TaskActivityAction.TaskCreated:
        return `${who}, "${log.newValue}" görevini oluşturdu.`;
      case TaskActivityAction.Assigned:
        if (log.newValue && log.oldValue) {
          return `${who}, atamayı "${log.oldValue}" → "${log.newValue}" olarak değiştirdi.`;
        }
        if (log.newValue) {
          return `${who}, görevi "${log.newValue}" kişisine atadı.`;
        }
        return `${who}, görevin atamasını kaldırdı.`;
      case TaskActivityAction.ColumnChanged:
        return `${who}, görevi "${log.oldValue}" sütunundan "${log.newValue}" sütununa taşıdı.`;
      case TaskActivityAction.Updated:
        return `${who}, "${log.newValue}" görevini güncelledi.`;
      case TaskActivityAction.Deleted:
        return `${who}, "${log.oldValue}" görevini sildi.`;
      case TaskActivityAction.AssignmentAccepted:
        return `${who}, görev atamasını onayladı.`;
      case TaskActivityAction.AssignmentDeclined:
        return `${who}, görev atamasını reddetti.`;
      case TaskActivityAction.AttachmentAdded:
        return `${who}, "${log.newValue}" dosyasını ekledi.`;
      case TaskActivityAction.AttachmentDeleted:
        return `${who}, "${log.oldValue}" dosyasını sildi.`;
      default:
        return `${who}, bir işlem yaptı.`;
    }
  }

  // ---- Column ----
  openColumnModal(): void {
    this.columnForm.reset({ title: '' });
    this.columnError.set(null);
    this.showColumnModal.set(true);
  }

  closeColumnModal(): void {
    if (this.savingColumn()) {
      return;
    }
    this.showColumnModal.set(false);
  }

  submitColumn(): void {
    const id = this.teamId();
    if (id === null || this.columnForm.invalid) {
      this.columnForm.markAllAsTouched();
      return;
    }

    this.savingColumn.set(true);
    this.columnError.set(null);

    const { title } = this.columnForm.getRawValue();
    this.teamService.addColumn(id, { title: title.trim() }).subscribe({
      next: () => {
        this.savingColumn.set(false);
        this.showColumnModal.set(false);
        this.load();
      },
      error: (err: HttpErrorResponse) => {
        this.savingColumn.set(false);
        this.columnError.set(err.error?.message ?? 'Sütun eklenemedi.');
      }
    });
  }

  startEditColumn(column: BoardColumnWithTasks): void {
    this.editColumnError.set(null);
    this.editColumnForm.setValue({ title: column.title });
    this.editingColumnId.set(column.id);
  }

  cancelEditColumn(): void {
    if (this.savingColumnEdit()) {
      return;
    }
    this.editingColumnId.set(null);
    this.editColumnError.set(null);
  }

  saveColumnEdit(column: BoardColumnWithTasks): void {
    const id = this.teamId();
    if (id === null || this.editColumnForm.invalid) {
      this.editColumnForm.markAllAsTouched();
      return;
    }

    const { title } = this.editColumnForm.getRawValue();
    const trimmed = title.trim();
    if (trimmed === column.title) {
      this.cancelEditColumn();
      return;
    }

    this.savingColumnEdit.set(true);
    this.editColumnError.set(null);

    this.teamService.updateColumn(id, column.id, { title: trimmed }).subscribe({
      next: () => {
        this.savingColumnEdit.set(false);
        this.editingColumnId.set(null);
        this.load();
      },
      error: (err: HttpErrorResponse) => {
        this.savingColumnEdit.set(false);
        this.editColumnError.set(err.error?.message ?? 'Sütun güncellenemedi.');
      }
    });
  }

  onColumnDragStart(columnId: number): void {
    if (!this.isLeader()) {
      return;
    }
    this.draggedColumnId.set(columnId);
  }

  onColumnDragEnd(): void {
    this.draggedColumnId.set(null);
    this.dragOverReorderColumnId.set(null);
  }

  // ---- Task ----
  openTaskModal(columnId: number): void {
    this.targetColumnId.set(columnId);
    this.taskForm.reset({
      title: '',
      description: '',
      priority: Priority.Medium,
      categoryId: '',
      dueDate: '',
      assignedToUserId: ''
    });
    this.taskError.set(null);
    this.showTaskModal.set(true);
  }

  toggleNewCategory(): void {
    this.showNewCategory.update((v) => !v);
    this.newCategoryError.set(null);
    this.newCategoryControl.reset('');
  }

  private resetNewCategory(): void {
    this.showNewCategory.set(false);
    this.newCategoryError.set(null);
    this.newCategoryControl.reset('');
  }

  createCategoryInline(): void {
    const name = this.newCategoryControl.getRawValue().trim();
    if (!name) {
      this.newCategoryError.set('Kategori adı boş olamaz.');
      return;
    }

    this.creatingCategory.set(true);
    this.newCategoryError.set(null);

    this.categoryService.create({ name }).subscribe({
      next: (category) => {
        this.creatingCategory.set(false);
        this.categories.update((list) => [...list, category]);
        this.detailForm.controls.categoryId.setValue(String(category.id));
        this.resetNewCategory();
      },
      error: (err: HttpErrorResponse) => {
        this.creatingCategory.set(false);
        this.newCategoryError.set(
          typeof err.error === 'string'
            ? err.error
            : (err.error?.message ?? 'Kategori eklenemedi.')
        );
      }
    });
  }

  closeTaskModal(): void {
    if (this.savingTask()) {
      return;
    }
    this.showTaskModal.set(false);
  }

  submitTask(): void {
    const id = this.teamId();
    const columnId = this.targetColumnId();
    if (id === null || columnId === null || this.taskForm.invalid) {
      this.taskForm.markAllAsTouched();
      return;
    }

    this.savingTask.set(true);
    this.taskError.set(null);

    const raw = this.taskForm.getRawValue();
    this.teamService
      .createTask(id, {
        title: raw.title.trim(),
        description: raw.description.trim() || null,
        categoryId: raw.categoryId ? Number(raw.categoryId) : null,
        priority: Number(raw.priority) as Priority,
        startDate: new Date().toISOString(),
        dueDate: raw.dueDate ? new Date(raw.dueDate).toISOString() : null,
        boardColumnId: columnId,
        assignedToUserId: raw.assignedToUserId ? Number(raw.assignedToUserId) : null
      })
      .subscribe({
        next: () => {
          this.savingTask.set(false);
          this.showTaskModal.set(false);
          this.load();
        },
        error: (err: HttpErrorResponse) => {
          this.savingTask.set(false);
          this.taskError.set(err.error?.message ?? 'Görev eklenemedi.');
        }
      });
  }

  completeTask(task: TaskListItem): void {
    const request = task.isCompleted
      ? this.taskService.reopen(task.id)
      : this.taskService.complete(task.id);

    request.subscribe({
      next: () => this.load(),
      error: () => this.load()
    });
  }

  deleteTask(task: TaskListItem): void {
    if (!confirm(`"${task.title}" görevi silinsin mi?`)) {
      return;
    }

    this.taskService.delete(task.id).subscribe({
      next: () => {
        if (this.detail()?.id === task.id) {
          this.showDetailModal.set(false);
          this.detail.set(null);
        }
        this.load();
      },
      error: () => this.load()
    });
  }

  // ---- Task detail / edit ----
  openTaskDetail(task: TaskListItem): void {
    this.showDetailModal.set(true);
    this.detailEditing.set(false);
    this.detailError.set(null);
    this.detailSaveError.set(null);
    this.detailLoading.set(true);
    this.detail.set(null);
    this.taskActivity.set([]);
    this.taskAttachments.set([]);
    this.selectedAttachmentId.set(null);
    this.revokeAttachmentPreviewUrls();

    this.taskService.getTask(task.id).subscribe({
      next: (detail) => {
        this.detail.set(detail);
        this.detailLoading.set(false);
        this.loadTaskActivity(task.id);
        this.loadTaskAttachments(task.id);
      },
      error: (err: HttpErrorResponse) => {
        this.detailLoading.set(false);
        this.detailError.set(err.error?.message ?? 'Görev detayı yüklenemedi.');
      }
    });
  }

  closeDetailModal(): void {
    if (this.savingDetail()) {
      return;
    }
    this.showDetailModal.set(false);
    this.detailEditing.set(false);
    this.detail.set(null);
    this.taskActivity.set([]);
    this.taskAttachments.set([]);
    this.selectedAttachmentId.set(null);
    this.revokeAttachmentPreviewUrls();
    this.closeAttachmentLightbox();
  }

  startDetailEdit(): void {
    const d = this.detail();
    if (!d) {
      return;
    }
    this.detailSaveError.set(null);
    this.detailForm.setValue({
      title: d.title,
      description: d.description ?? '',
      priority: d.priority,
      categoryId: d.categoryId != null ? String(d.categoryId) : '',
      startDate: this.toDateInput(d.startDate),
      dueDate: d.dueDate ? this.toDateInput(d.dueDate) : ''
    });
    this.resetNewCategory();
    this.detailEditing.set(true);
  }

  cancelDetailEdit(): void {
    if (this.savingDetail()) {
      return;
    }
    this.detailEditing.set(false);
    this.detailSaveError.set(null);
    this.resetNewCategory();
  }

  saveDetail(): void {
    const d = this.detail();
    if (!d || this.detailForm.invalid) {
      this.detailForm.markAllAsTouched();
      return;
    }

    this.savingDetail.set(true);
    this.detailSaveError.set(null);

    const raw = this.detailForm.getRawValue();
    this.taskService
      .updateTask(d.id, {
        title: raw.title.trim(),
        description: raw.description.trim() || null,
        categoryId: raw.categoryId ? Number(raw.categoryId) : null,
        priority: Number(raw.priority) as Priority,
        startDate: new Date(raw.startDate).toISOString(),
        dueDate: raw.dueDate ? new Date(raw.dueDate).toISOString() : null
      })
      .subscribe({
        next: () => {
          this.savingDetail.set(false);
          this.detailEditing.set(false);
          this.refreshDetail(d.id);
          this.load();
        },
        error: (err: HttpErrorResponse) => {
          this.savingDetail.set(false);
          this.detailSaveError.set(err.error?.message ?? 'Görev güncellenemedi.');
        }
      });
  }

  changeAssignee(value: string): void {
    const d = this.detail();
    if (!d) {
      return;
    }

    const assignedToUserId = value ? Number(value) : null;
    this.assigning.set(true);
    this.taskService.assign(d.id, assignedToUserId).subscribe({
      next: () => {
        this.assigning.set(false);
        this.refreshDetail(d.id);
        this.load();
      },
      error: () => {
        this.assigning.set(false);
        this.refreshDetail(d.id);
      }
    });
  }

  toggleDetailComplete(): void {
    const d = this.detail();
    if (!d) {
      return;
    }

    const request = d.isCompleted ? this.taskService.reopen(d.id) : this.taskService.complete(d.id);
    request.subscribe({
      next: () => {
        this.refreshDetail(d.id);
        this.load();
      },
      error: () => this.refreshDetail(d.id)
    });
  }

  private refreshDetail(taskId: number): void {
    this.taskService.getTask(taskId).subscribe({
      next: (detail) => this.detail.set(detail),
      error: () => {}
    });
    this.loadTaskActivity(taskId);
    this.loadTaskAttachments(taskId);
  }

  private toDateInput(iso: string): string {
    return iso ? iso.substring(0, 10) : '';
  }

  // ---- Drag & drop ----
  onDragStart(event: DragEvent, task: TaskListItem): void {
    const sourceColumn = this.board()?.columns.find((column) =>
      column.tasks.some((item) => item.id === task.id)
    );

    this.draggedTaskId.set(task.id);
    this.dragOriginColumnId = sourceColumn?.id ?? null;
    this.dragOverColumnId.set(sourceColumn?.id ?? null);
    this.lastBroadcastHoverColumnId = sourceColumn?.id ?? null;

    const teamId = this.teamId();
    if (teamId !== null && sourceColumn) {
      void this.boardHub.notifyTaskDragStart(teamId, task.id, sourceColumn.id);
    }

    if (!event.dataTransfer) {
      return;
    }

    event.dataTransfer.effectAllowed = 'move';
    event.dataTransfer.setData('text/plain', String(task.id));

    const card = event.currentTarget as HTMLElement | null;
    if (!card) {
      return;
    }

    const ghost = card.cloneNode(true) as HTMLElement;
    ghost.style.width = `${card.offsetWidth}px`;
    ghost.style.position = 'absolute';
    ghost.style.top = '-9999px';
    ghost.style.left = '-9999px';
    ghost.style.opacity = '0.95';
    document.body.appendChild(ghost);
    event.dataTransfer.setDragImage(ghost, card.offsetWidth / 2, 24);
    requestAnimationFrame(() => ghost.remove());
  }

  onDragEnd(): void {
    const taskId = this.draggedTaskId();
    const teamId = this.teamId();
    if (taskId !== null && teamId !== null) {
      void this.boardHub.notifyTaskDragEnd(teamId, taskId);
    }

    this.draggedTaskId.set(null);
    this.dragOverColumnId.set(null);
    this.dragOriginColumnId = null;
    this.lastBroadcastHoverColumnId = null;
  }

  onDragOver(event: DragEvent, columnId: number): void {
    event.preventDefault();
    if (this.draggedColumnId() !== null) {
      this.dragOverReorderColumnId.set(columnId);
      return;
    }

    if (event.dataTransfer) {
      event.dataTransfer.dropEffect = 'move';
    }

    const taskId = this.draggedTaskId();
    const teamId = this.teamId();
    if (
      taskId !== null &&
      teamId !== null &&
      this.lastBroadcastHoverColumnId !== columnId
    ) {
      this.lastBroadcastHoverColumnId = columnId;
      void this.boardHub.notifyTaskDragMove(teamId, taskId, columnId);
    }

    this.dragOverColumnId.set(columnId);
  }

  onColumnDragLeave(columnId: number): void {
    if (this.dragOverColumnId() === columnId) {
      this.dragOverColumnId.set(this.dragOriginColumnId);
    }

    if (this.dragOverReorderColumnId() === columnId) {
      this.dragOverReorderColumnId.set(null);
    }
  }

  draggedTask(): TaskListItem | null {
    const taskId = this.draggedTaskId();
    if (taskId === null) {
      return null;
    }

    for (const column of this.board()?.columns ?? []) {
      const task = column.tasks.find((item) => item.id === taskId);
      if (task) {
        return task;
      }
    }

    return null;
  }

  showTaskDropPreview(column: BoardColumnWithTasks): boolean {
    const taskId = this.draggedTaskId();
    const hoverColumnId = this.dragOverColumnId();
    if (taskId === null || hoverColumnId === null || hoverColumnId !== column.id) {
      return false;
    }

    return !column.tasks.some((task) => task.id === taskId);
  }

  remoteDropPreviews(columnId: number): RemoteTaskDrag[] {
    return this.remoteTaskDrags().filter(
      (drag) => drag.hoverColumnId === columnId && drag.hoverColumnId !== drag.sourceColumnId
    );
  }

  isRemoteDraggingTask(taskId: number, columnId: number): boolean {
    return this.remoteTaskDrags().some(
      (drag) =>
        drag.taskId === taskId &&
        drag.sourceColumnId === columnId &&
        drag.hoverColumnId !== columnId
    );
  }

  remoteDragInPlace(taskId: number): RemoteTaskDrag | null {
    return (
      this.remoteTaskDrags().find(
        (drag) => drag.taskId === taskId && drag.hoverColumnId === drag.sourceColumnId
      ) ?? null
    );
  }

  taskById(taskId: number): TaskListItem | null {
    for (const column of this.board()?.columns ?? []) {
      const task = column.tasks.find((item) => item.id === taskId);
      if (task) {
        return task;
      }
    }

    return null;
  }

  remoteDragLabel(userId: number): string {
    const member = this.team()?.members.find((item) => item.userId === userId);
    return member ? this.memberName(member) : 'Bir kullanıcı';
  }

  private upsertRemoteDrag(drag: RemoteTaskDrag): void {
    this.remoteTaskDrags.update((list) => {
      const index = list.findIndex((item) => item.userId === drag.userId);
      if (index < 0) {
        return [...list, drag];
      }

      const next = [...list];
      next[index] = { ...next[index], ...drag };
      return next;
    });
  }

  private removeRemoteDrag(userId: number): void {
    this.remoteTaskDrags.update((list) => list.filter((item) => item.userId !== userId));
  }

  private findRemoteDrag(userId: number): RemoteTaskDrag | undefined {
    return this.remoteTaskDrags().find((item) => item.userId === userId);
  }

  onDrop(event: DragEvent, column: BoardColumnWithTasks): void {
    event.preventDefault();
    const draggedColumnId = this.draggedColumnId();
    if (draggedColumnId !== null) {
      this.dragOverReorderColumnId.set(null);
      this.draggedColumnId.set(null);

      if (draggedColumnId === column.id) {
        return;
      }

      const orderedColumnIds = this.reorderColumnsLocally(draggedColumnId, column.id);
      if (!orderedColumnIds) {
        return;
      }

      const id = this.teamId();
      if (id === null) {
        return;
      }

      this.teamService.reorderColumns(id, { columnIds: orderedColumnIds }).subscribe({
        next: () => this.load(),
        error: () => this.load()
      });
      return;
    }

    const taskId = this.draggedTaskId();
    this.dragOverColumnId.set(null);

    if (taskId === null) {
      return;
    }

    const source = this.board()?.columns.find((c) => c.tasks.some((t) => t.id === taskId));
    if (!source || source.id === column.id) {
      return;
    }

    this.draggedTaskId.set(null);
    this.dragOriginColumnId = null;
    this.lastBroadcastHoverColumnId = null;

    this.moveTaskLocally(taskId, source.id, column.id);

    this.taskService.moveToColumn(taskId, column.id).subscribe({
      next: () => this.load(),
      error: () => this.load()
    });
  }

  private moveTaskLocally(taskId: number, fromColumnId: number, toColumnId: number): void {
    const current = this.board();
    if (!current) {
      return;
    }

    const targetColumn = current.columns.find((col) => col.id === toColumnId);
    if (!targetColumn) {
      return;
    }

    let moved: TaskListItem | undefined;
    const columns = current.columns.map((col) => {
      if (col.id === fromColumnId) {
        moved = col.tasks.find((t) => t.id === taskId);
        return { ...col, tasks: col.tasks.filter((t) => t.id !== taskId) };
      }
      return col;
    });

    if (!moved) {
      return;
    }

    const movedTask: TaskListItem = {
      ...moved,
      boardColumnId: targetColumn.id,
      boardColumnTitle: targetColumn.title,
      isCompleted: targetColumn.isCompletedColumn
    };

    const withMoved = columns.map((col) =>
      col.id === toColumnId ? { ...col, tasks: [...col.tasks, movedTask] } : col
    );

    this.board.set({ ...current, columns: withMoved });
  }

  private reorderColumnsLocally(fromColumnId: number, toColumnId: number): number[] | null {
    const current = this.board();
    if (!current) {
      return null;
    }

    const columns = [...current.columns];
    const fromIndex = columns.findIndex((col) => col.id === fromColumnId);
    const toIndex = columns.findIndex((col) => col.id === toColumnId);
    if (fromIndex < 0 || toIndex < 0) {
      return null;
    }

    const [moved] = columns.splice(fromIndex, 1);
    columns.splice(toIndex, 0, moved);

    const reordered = columns.map((col, index) => ({ ...col, displayOrder: index }));
    this.board.set({ ...current, columns: reordered });
    return reordered.map((col) => col.id);
  }

  // ---- Members ----
  openMembersModal(): void {
    this.memberForm.reset({ email: '', searchQuery: '' });
    this.memberError.set(null);
    this.memberSearchResults.set([]);
    this.showMemberSearchResults.set(false);
    this.showMembersModal.set(true);
  }

  closeMembersModal(): void {
    if (this.savingMember()) {
      return;
    }
    this.showMembersModal.set(false);
    this.closeMemberSearchResults();
  }

  onMemberSearchFocus(): void {
    if (this.memberForm.controls.searchQuery.value.trim().length >= 3) {
      this.showMemberSearchResults.set(true);
    }
  }

  onMemberSearchInput(): void {
    this.memberForm.controls.email.setValue('', { emitEvent: false });
    this.memberError.set(null);
  }

  closeMemberSearchResults(): void {
    this.showMemberSearchResults.set(false);
  }

  selectMemberFromSearch(user: UserSearchResult): void {
    this.memberForm.patchValue(
      {
        email: user.email,
        searchQuery: user.displayName
      },
      { emitEvent: false }
    );
    this.memberSearchResults.set([]);
    this.showMemberSearchResults.set(false);
    this.memberError.set(null);
  }

  submitMember(): void {
    const id = this.teamId();
    if (id === null || this.memberForm.invalid) {
      this.memberForm.markAllAsTouched();
      return;
    }

    this.savingMember.set(true);
    this.memberError.set(null);

    const { email } = this.memberForm.getRawValue();
    this.teamService.addMember(id, { email: email.trim() }).subscribe({
      next: () => {
        this.savingMember.set(false);
        this.memberForm.reset({ email: '', searchQuery: '' });
        this.memberSearchResults.set([]);
        this.showMemberSearchResults.set(false);
        this.reloadTeam();
      },
      error: (err: HttpErrorResponse) => {
        this.savingMember.set(false);
        this.memberError.set(err.error?.message ?? 'Üye eklenemedi.');
      }
    });
  }

  removeMember(userId: number): void {
    const id = this.teamId();
    if (id === null) {
      return;
    }

    if (!confirm('Bu üye takımdan çıkarılsın mı?')) {
      return;
    }

    this.teamService.removeMember(id, userId).subscribe({
      next: () => this.reloadTeam(),
      error: (err: HttpErrorResponse) => this.memberError.set(err.error?.message ?? 'Üye çıkarılamadı.')
    });
  }

  private reloadTeam(): void {
    const id = this.teamId();
    if (id === null) {
      return;
    }
    this.teamService.getTeam(id).subscribe({
      next: (team) => this.team.set(team),
      error: () => {}
    });
  }

  // ---- Team ----
  deleteTeam(): void {
    const id = this.teamId();
    if (id === null) {
      return;
    }

    if (!confirm('Bu takım ve tüm panosu kalıcı olarak silinsin mi?')) {
      return;
    }

    this.deletingTeam.set(true);
    this.teamService.deleteTeam(id).subscribe({
      next: () => {
        this.deletingTeam.set(false);
        void this.router.navigate(['/teams']);
      },
      error: (err: HttpErrorResponse) => {
        this.deletingTeam.set(false);
        this.error.set(err.error?.message ?? 'Takım silinemedi.');
      }
    });
  }

  priorityLabel(priority: Priority): string {
    return this.priorityOptions.find((p) => p.value === priority)?.label ?? '';
  }

  priorityClass(priority: Priority): string {
    switch (priority) {
      case Priority.Critical:
        return 'critical';
      case Priority.High:
        return 'high';
      case Priority.Medium:
        return 'medium';
      default:
        return 'low';
    }
  }

  initial(value: string): string {
    return value.trim().charAt(0).toUpperCase() || '?';
  }

  memberName(member: TeamMember): string {
    const full = [member.firstName, member.lastName].filter((part) => !!part && part.trim()).join(' ').trim();
    return full || member.email;
  }
}
