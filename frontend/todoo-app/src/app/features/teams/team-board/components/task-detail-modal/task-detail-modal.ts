import { DatePipe, NgTemplateOutlet } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import {
  Component,
  DestroyRef,
  HostListener,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
  untracked
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';
import { RouterLink } from '@angular/router';
import { concatMap, from, of, switchMap, toArray } from 'rxjs';
import { AuthService } from '../../../../../core/services/auth.service';
import { CategoryService } from '../../../../../core/services/category.service';
import { RecentItemsService } from '../../../../../core/services/recent-items.service';
import { TaskService } from '../../../../../core/services/task.service';
import { TeamBoardHubService } from '../../../../../core/services/team-board-hub.service';
import { Category } from '../../../../../models/category.model';
import {
  AssignmentStatus,
  BoardColumn,
  CommentAttachment,
  CreateCommentRequest,
  Priority,
  TaskActivityAction,
  TaskAttachment,
  TaskComment,
  TaskDetail,
  TeamActivityLog,
  TeamMember
} from '../../../../../models/team.model';
import {
  buildMentionText,
  DraftMention,
  encodeCommentBody,
  parseCommentBody,
  syncDraftMentions
} from '../../comment-mention.utils';
import { initial, memberName, PRIORITY_OPTIONS, priorityClass, priorityLabel } from '../../board-ui.utils';

@Component({
  selector: 'app-task-detail-modal',
  imports: [ReactiveFormsModule, DatePipe, NgTemplateOutlet, RouterLink],
  templateUrl: './task-detail-modal.html',
  styleUrl: './task-detail-modal.scss'
})
export class TaskDetailModalComponent {
  private readonly fb = inject(FormBuilder);
  private readonly taskService = inject(TaskService);
  private readonly categoryService = inject(CategoryService);
  private readonly auth = inject(AuthService);
  private readonly recentItems = inject(RecentItemsService);
  private readonly boardHub = inject(TeamBoardHubService);
  private readonly sanitizer = inject(DomSanitizer);
  private readonly destroyRef = inject(DestroyRef);

  private readonly user = this.auth.getUser();

  readonly taskId = input.required<number>();
  readonly boardName = input<string>('Pano');
  readonly boardColumns = input<BoardColumn[]>([]);
  readonly categories = input<Category[]>([]);
  readonly members = input<TeamMember[]>([]);
  readonly isLeader = input(false);
  readonly photoUrlFn = input.required<(userId: number | null | undefined) => string | null>();

  readonly closed = output<void>();
  readonly changed = output<void>();
  readonly categoryCreated = output<Category>();

  readonly detailLoading = signal(false);
  readonly detail = signal<TaskDetail | null>(null);
  readonly detailError = signal<string | null>(null);
  readonly detailEditing = signal(false);
  readonly savingDetail = signal(false);
  readonly detailSaveError = signal<string | null>(null);
  readonly assigning = signal(false);
  readonly respondingToAssignment = signal(false);

  readonly taskActivity = signal<TeamActivityLog[]>([]);
  readonly taskActivityLoading = signal(false);

  readonly taskAttachments = signal<TaskAttachment[]>([]);
  readonly taskAttachmentsLoading = signal(false);
  readonly uploadingAttachment = signal(false);
  readonly attachmentDragOver = signal(false);
  readonly attachmentError = signal<string | null>(null);
  readonly deletingAttachmentId = signal<number | null>(null);
  readonly attachmentPreviewUrls = signal<Record<number, string>>({});
  readonly trustedPreviewUrls = signal<Record<number, SafeResourceUrl>>({});
  readonly attachmentPreviewLoadingIds = signal<Set<number>>(new Set());
  readonly selectedAttachmentId = signal<number | null>(null);
  readonly attachmentLightbox = signal<TaskAttachment | null>(null);
  readonly attachmentLightboxLoading = signal(false);
  private attachmentPreviewGeneration = 0;

  readonly taskComments = signal<TaskComment[]>([]);
  readonly taskCommentsLoading = signal(false);
  readonly postingComment = signal(false);
  readonly commentError = signal<string | null>(null);
  readonly replyToCommentId = signal<number | null>(null);
  readonly pendingCommentFiles = signal<File[]>([]);
  readonly deletingCommentId = signal<number | null>(null);
  readonly deletingCommentAttachmentKey = signal<string | null>(null);

  readonly leftPaneTab = signal<'comments' | 'activity'>('comments');
  readonly attachmentsExpanded = signal(true);
  readonly descriptionExpanded = signal(true);
  readonly activityExpanded = signal(false);
  readonly detailsExpanded = signal(true);
  readonly showColumnMenu = signal(false);
  readonly movingColumn = signal(false);

  readonly showNewCategory = signal(false);
  readonly creatingCategory = signal(false);
  readonly newCategoryError = signal<string | null>(null);
  readonly newCategoryControl = this.fb.nonNullable.control('', [Validators.maxLength(100)]);

  readonly commentForm = this.fb.nonNullable.group({
    body: ['', [Validators.maxLength(4000)]]
  });

  readonly mentionPickerOpen = signal(false);
  readonly mentionQuery = signal('');
  readonly mentionStartIndex = signal(0);
  readonly mentionCursorIndex = signal(0);
  readonly mentionActiveIndex = signal(0);
  readonly draftMentions = signal<DraftMention[]>([]);
  readonly commentDraftBody = signal('');

  readonly filteredMentionMembers = computed(() => {
    const query = this.normalizeMentionQuery(this.mentionQuery());
    const members = this.members();

    if (!query) {
      return members;
    }

    return members.filter((member) => {
      const searchable = [
        memberName(member),
        member.firstName ?? '',
        member.lastName ?? '',
        member.email
      ]
        .map((value) => this.normalizeMentionQuery(value))
        .filter((value) => value.length > 0);

      return searchable.some((value) => value.includes(query));
    });
  });

  readonly detailForm = this.fb.nonNullable.group({
    title: ['', [Validators.required, Validators.maxLength(200)]],
    description: [''],
    priority: [Priority.Medium, [Validators.required]],
    categoryId: [''],
    startDate: ['', [Validators.required]],
    dueDate: ['']
  });

  readonly selectedAttachment = computed(() => {
    const id = this.selectedAttachmentId();
    if (id == null) {
      return null;
    }
    return this.taskAttachments().find((attachment) => attachment.id === id) ?? null;
  });

  readonly assignmentStatus = AssignmentStatus;
  readonly priorityOptions = PRIORITY_OPTIONS;
  readonly priorityLabel = priorityLabel;
  readonly priorityClass = priorityClass;
  readonly initial = initial;
  readonly memberName = memberName;

  private backdropCloseArmed = false;

  constructor() {
    effect(() => {
      const id = this.taskId();
      // Side-effect writes must stay untracked so the effect does not re-run
      // on every detail/loading signal update (can freeze the UI in zoneless mode).
      untracked(() => this.loadTask(id));
    });

    // Arm backdrop close after the opening click has fully finished.
    queueMicrotask(() => {
      this.backdropCloseArmed = true;
    });

    this.boardHub.boardChanged$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((event) => {
      const openDetailId = this.detail()?.id;
      if (openDetailId == null) {
        return;
      }

      const affectsOpenDetail = event.taskId == null || event.taskId === openDetailId;
      if (!affectsOpenDetail) {
        return;
      }

      if (event.actorUserId !== this.user?.userId) {
        this.refreshDetail(openDetailId);
      }

      this.loadTaskActivity(openDetailId);
      this.loadTaskAttachments(openDetailId);
      this.loadTaskComments(openDetailId);
    });

    this.destroyRef.onDestroy(() => this.revokeAttachmentPreviewUrls());
  }

  @HostListener('document:keydown.escape')
  onEscapeKey(): void {
    if (this.attachmentLightbox()) {
      this.closeAttachmentLightbox();
    }
  }

  @HostListener('document:click')
  onDocumentClick(): void {
    if (this.showColumnMenu()) {
      this.closeColumnMenu();
    }

    if (this.mentionPickerOpen()) {
      this.closeMentionPicker();
    }
  }

  private loadTask(taskId: number): void {
    this.detailEditing.set(false);
    this.detailError.set(null);
    this.detailSaveError.set(null);
    this.detailLoading.set(true);
    this.detail.set(null);
    this.taskActivity.set([]);
    this.taskAttachments.set([]);
    this.selectedAttachmentId.set(null);
    this.taskComments.set([]);
    this.replyToCommentId.set(null);
    this.pendingCommentFiles.set([]);
    this.leftPaneTab.set('comments');
    this.attachmentsExpanded.set(true);
    this.descriptionExpanded.set(true);
    this.activityExpanded.set(false);
    this.detailsExpanded.set(true);
    this.showColumnMenu.set(false);
    this.movingColumn.set(false);
    this.commentForm.reset({ body: '' });
    this.commentDraftBody.set('');
    this.draftMentions.set([]);
    this.closeMentionPicker();
    this.revokeAttachmentPreviewUrls();

    this.taskService.getTask(taskId).subscribe({
      next: (detail) => {
        this.detail.set(detail);
        this.detailLoading.set(false);
        this.recentItems.recordTask({
          taskId: detail.id,
          title: detail.title,
          teamId: detail.teamId,
          teamName: detail.teamName ?? '',
          boardId: detail.boardId,
          boardName: this.boardName(),
          boardColumnTitle: detail.boardColumnTitle
        });
        this.loadTaskActivity(taskId);
        this.loadTaskAttachments(taskId);
        this.loadTaskComments(taskId);
      },
      error: (err: HttpErrorResponse) => {
        this.detailLoading.set(false);
        this.detailError.set(err.error?.message ?? 'Görev detayı yüklenemedi.');
      }
    });
  }

  private refreshDetail(taskId: number): void {
    this.taskService.getTask(taskId).subscribe({
      next: (detail) => this.detail.set(detail),
      error: () => {}
    });
    this.loadTaskActivity(taskId);
    this.loadTaskAttachments(taskId);
    this.loadTaskComments(taskId);
  }

  close(): void {
    if (!this.backdropCloseArmed || this.savingDetail()) {
      return;
    }
    this.closed.emit();
  }

  memberPhotoUrl(userId: number | null | undefined): string | null {
    return this.photoUrlFn()(userId);
  }

  canRespondToAssignment(detail: TaskDetail): boolean {
    return (
      detail.assignedToUserId === this.user?.userId && detail.assignmentStatus === AssignmentStatus.Pending
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
        this.changed.emit();
        this.refreshDetail(detail.id);
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
        this.changed.emit();
        this.refreshDetail(detail.id);
      },
      error: (err: HttpErrorResponse) => {
        this.respondingToAssignment.set(false);
        this.detailError.set(err.error?.message ?? 'Görev reddedilemedi.');
      }
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
    const generation = ++this.attachmentPreviewGeneration;
    const previewableAttachments = attachments.filter(
      (attachment) => this.isImageAttachment(attachment) || this.isPdfAttachment(attachment)
    );
    const previewableIds = new Set(previewableAttachments.map((attachment) => attachment.id));

    this.pruneAttachmentPreviewUrls(previewableIds);

    const loadingIds = new Set<number>();
    for (const attachment of previewableAttachments) {
      if (this.attachmentPreviewUrl(attachment.id)) {
        continue;
      }

      loadingIds.add(attachment.id);
      this.taskService.downloadAttachment(taskId, attachment.id).subscribe({
        next: (blob) => {
          if (generation !== this.attachmentPreviewGeneration) {
            return;
          }

          this.clearAttachmentPreviewLoading(attachment.id);
          if (!this.isUsablePreviewBlob(attachment, blob)) {
            return;
          }

          this.registerPreviewBlob(attachment, blob);
        },
        error: () => {
          if (generation !== this.attachmentPreviewGeneration) {
            return;
          }
          this.clearAttachmentPreviewLoading(attachment.id);
        }
      });
    }

    this.attachmentPreviewLoadingIds.set(loadingIds);
  }

  private pruneAttachmentPreviewUrls(validIds: Set<number>): void {
    this.attachmentPreviewUrls.update((current) => {
      const next: Record<number, string> = {};
      for (const [id, url] of Object.entries(current)) {
        const attachmentId = Number(id);
        if (validIds.has(attachmentId)) {
          next[attachmentId] = url;
        } else {
          URL.revokeObjectURL(url);
        }
      }
      return next;
    });

    this.trustedPreviewUrls.update((current) => {
      const next: Record<number, SafeResourceUrl> = {};
      for (const [id, url] of Object.entries(current)) {
        const attachmentId = Number(id);
        if (validIds.has(attachmentId)) {
          next[attachmentId] = url;
        }
      }
      return next;
    });
  }

  private clearAttachmentPreviewLoading(attachmentId: number): void {
    this.attachmentPreviewLoadingIds.update((current) => {
      if (!current.has(attachmentId)) {
        return current;
      }
      const next = new Set(current);
      next.delete(attachmentId);
      return next;
    });
  }

  attachmentPreviewLoading(attachmentId: number): boolean {
    return this.attachmentPreviewLoadingIds().has(attachmentId);
  }

  onAttachmentThumbError(attachmentId: number): void {
    const url = this.attachmentPreviewUrls()[attachmentId];
    if (url) {
      URL.revokeObjectURL(url);
    }

    this.attachmentPreviewUrls.update((current) => {
      const next = { ...current };
      delete next[attachmentId];
      return next;
    });

    this.trustedPreviewUrls.update((current) => {
      const next = { ...current };
      delete next[attachmentId];
      return next;
    });
  }

  private isUsablePreviewBlob(attachment: TaskAttachment, blob: Blob): boolean {
    if (!blob || blob.size === 0) {
      return false;
    }

    const mime = (blob.type || attachment.contentType || '').toLowerCase();
    if (mime.includes('json') || mime.includes('html') || mime.includes('text/plain')) {
      return false;
    }

    if (this.isImageAttachment(attachment) && blob.size < 64) {
      return false;
    }

    if (this.isPdfAttachment(attachment) && blob.size < 5) {
      return false;
    }

    return true;
  }

  private registerPreviewBlob(attachment: TaskAttachment, blob: Blob): void {
    const contentType = this.resolvePreviewContentType(attachment, blob);
    const typedBlob = blob.type === contentType ? blob : new Blob([blob], { type: contentType });
    const url = URL.createObjectURL(typedBlob);

    const existingUrl = this.attachmentPreviewUrls()[attachment.id];
    if (existingUrl) {
      URL.revokeObjectURL(existingUrl);
    }

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
    this.ensureAttachmentPreview(attachment);
  }

  private ensureAttachmentPreview(attachment: TaskAttachment): void {
    const detail = this.detail();
    if (!detail) {
      return;
    }

    if (!this.isImageAttachment(attachment) && !this.isPdfAttachment(attachment)) {
      return;
    }

    if (this.attachmentPreviewUrl(attachment.id)) {
      return;
    }

    this.taskService.downloadAttachment(detail.id, attachment.id).subscribe({
      next: (blob) => this.registerPreviewBlob(attachment, blob),
      error: () => {}
    });
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
    const inputEl = event.target as HTMLInputElement;
    const file = inputEl.files?.[0];
    inputEl.value = '';
    if (!file) {
      return;
    }
    this.uploadAttachmentFile(file);
  }

  onAttachmentDragOver(event: DragEvent): void {
    if (this.uploadingAttachment()) {
      return;
    }
    event.preventDefault();
    event.stopPropagation();
    if (event.dataTransfer) {
      event.dataTransfer.dropEffect = 'copy';
    }
    this.attachmentDragOver.set(true);
  }

  onAttachmentDragLeave(event: DragEvent): void {
    event.preventDefault();
    event.stopPropagation();
    const related = event.relatedTarget as Node | null;
    const current = event.currentTarget as HTMLElement;
    if (!related || !current.contains(related)) {
      this.attachmentDragOver.set(false);
    }
  }

  onAttachmentDrop(event: DragEvent): void {
    event.preventDefault();
    event.stopPropagation();
    this.attachmentDragOver.set(false);
    if (this.uploadingAttachment()) {
      return;
    }
    const file = event.dataTransfer?.files?.[0];
    if (!file) {
      return;
    }
    this.uploadAttachmentFile(file);
  }

  private uploadAttachmentFile(file: File): void {
    const detail = this.detail();
    if (!detail) {
      return;
    }

    this.uploadingAttachment.set(true);
    this.attachmentError.set(null);

    this.taskService.uploadAttachment(detail.id, file).subscribe({
      next: () => {
        this.uploadingAttachment.set(false);
        this.attachmentsExpanded.set(true);
        this.loadTaskAttachments(detail.id);
        this.loadTaskActivity(detail.id);
      },
      error: (err: HttpErrorResponse) => {
        this.uploadingAttachment.set(false);
        this.attachmentError.set(err.error?.message ?? 'Dosya yüklenemedi.');
      }
    });
  }

  private loadTaskComments(taskId: number): void {
    this.taskCommentsLoading.set(true);
    this.commentError.set(null);
    this.taskService.listComments(taskId).subscribe({
      next: (comments) => {
        this.taskComments.set(comments);
        this.taskCommentsLoading.set(false);
      },
      error: (err: HttpErrorResponse) => {
        this.taskComments.set([]);
        this.taskCommentsLoading.set(false);
        this.commentError.set(err.error?.message ?? 'Yorumlar yüklenemedi.');
      }
    });
  }

  startReplyToComment(commentId: number): void {
    this.replyToCommentId.set(commentId);
    this.leftPaneTab.set('comments');
    this.commentError.set(null);
  }

  cancelReply(): void {
    this.replyToCommentId.set(null);
  }

  setLeftPaneTab(tab: 'comments' | 'activity'): void {
    this.leftPaneTab.set(tab);
  }

  toggleAttachmentsPanel(): void {
    this.attachmentsExpanded.update((expanded) => !expanded);
  }

  toggleDescriptionSection(): void {
    this.descriptionExpanded.update((expanded) => !expanded);
  }

  toggleActivitySection(): void {
    this.activityExpanded.update((expanded) => !expanded);
  }

  toggleDetailsSection(): void {
    this.detailsExpanded.update((expanded) => !expanded);
  }

  onCommentFilesSelected(event: Event): void {
    const inputEl = event.target as HTMLInputElement;
    const files = inputEl.files ? Array.from(inputEl.files) : [];
    inputEl.value = '';
    if (files.length === 0) {
      return;
    }
    this.pendingCommentFiles.update((current) => [...current, ...files]);
  }

  removePendingCommentFile(index: number): void {
    this.pendingCommentFiles.update((current) => current.filter((_, i) => i !== index));
  }

  commentBodySegments(body: string) {
    return parseCommentBody(body);
  }

  onCommentInput(event: Event): void {
    const textarea = event.target as HTMLTextAreaElement;
    const value = textarea.value;
    const previousBody = this.commentDraftBody();
    this.draftMentions.set(syncDraftMentions(previousBody, value, this.draftMentions()));
    this.commentDraftBody.set(value);
    this.commentForm.controls.body.setValue(value, { emitEvent: false });
    const cursor = textarea.selectionStart ?? value.length;
    const beforeCursor = value.slice(0, cursor);
    const mentionMatch = beforeCursor.match(/@([^\s@{}]*)$/);

    if (mentionMatch) {
      this.mentionPickerOpen.set(true);
      this.mentionQuery.set(mentionMatch[1]);
      this.mentionStartIndex.set(cursor - mentionMatch[0].length);
      this.mentionCursorIndex.set(cursor);
      this.mentionActiveIndex.set(0);
      return;
    }

    this.closeMentionPicker();
  }

  onCommentKeydown(event: KeyboardEvent): void {
    if (!this.mentionPickerOpen()) {
      return;
    }

    const members = this.filteredMentionMembers();
    if (event.key === 'ArrowDown') {
      if (members.length === 0) {
        return;
      }
      event.preventDefault();
      this.mentionActiveIndex.update((index) => (index + 1) % members.length);
      return;
    }

    if (event.key === 'ArrowUp') {
      if (members.length === 0) {
        return;
      }
      event.preventDefault();
      this.mentionActiveIndex.update((index) => (index - 1 + members.length) % members.length);
      return;
    }

    if (event.key === 'Enter' || event.key === 'Tab') {
      if (members.length === 0) {
        return;
      }
      event.preventDefault();
      this.insertMention(members[this.mentionActiveIndex()], event.target as HTMLTextAreaElement);
      return;
    }

    if (event.key === 'Escape') {
      event.preventDefault();
      this.closeMentionPicker();
    }
  }

  selectMentionMember(member: TeamMember, textarea?: HTMLTextAreaElement): void {
    this.insertMention(member, textarea);
  }

  setMentionActiveIndex(index: number): void {
    this.mentionActiveIndex.set(index);
  }

  private insertMention(member: TeamMember, textarea?: HTMLTextAreaElement): void {
    const start = this.mentionStartIndex();
    const cursor = this.mentionCursorIndex();
    const body = this.commentForm.controls.body.value;
    const displayName = memberName(member);
    const mentionText = buildMentionText(displayName);
    const nextBody = `${body.slice(0, start)}${mentionText} ${body.slice(cursor)}`;
    const nextCursor = start + mentionText.length + 1;

    this.commentForm.controls.body.setValue(nextBody);
    this.commentDraftBody.set(nextBody);
    this.draftMentions.update((mentions) => [
      ...syncDraftMentions(body, nextBody, mentions),
      {
        start,
        end: start + mentionText.length,
        userId: member.userId,
        displayName
      }
    ]);
    this.closeMentionPicker();

    if (textarea) {
      queueMicrotask(() => {
        textarea.focus();
        textarea.setSelectionRange(nextCursor, nextCursor);
      });
    }
  }

  private closeMentionPicker(): void {
    this.mentionPickerOpen.set(false);
    this.mentionQuery.set('');
    this.mentionActiveIndex.set(0);
  }

  private normalizeMentionQuery(value: string | null | undefined): string {
    return (value ?? '')
      .trim()
      .toLocaleLowerCase('tr-TR')
      .normalize('NFD')
      .replace(/\p{Diacritic}/gu, '');
  }

  submitComment(): void {
    const detail = this.detail();
    if (!detail || this.postingComment()) {
      return;
    }

    const body = this.commentForm.controls.body.value.trim();
    const files = this.pendingCommentFiles();
    if (!body && files.length === 0) {
      this.commentError.set('Yorum yazın veya dosya ekleyin.');
      return;
    }

    const encodedBody = encodeCommentBody(body, this.draftMentions());
    const request: CreateCommentRequest = {
      body: encodedBody || (files.length > 0 ? '(dosya eki)' : ''),
      parentCommentId: this.replyToCommentId()
    };

    this.postingComment.set(true);
    this.commentError.set(null);

    this.taskService
      .createComment(detail.id, request)
      .pipe(
        switchMap((comment) => {
          if (files.length === 0) {
            return of(comment);
          }
          return from(files).pipe(
            concatMap((file) => this.taskService.uploadCommentAttachment(detail.id, comment.id, file)),
            toArray(),
            switchMap(() => of(comment))
          );
        })
      )
      .subscribe({
        next: () => {
          this.postingComment.set(false);
          this.commentForm.reset({ body: '' });
          this.commentDraftBody.set('');
          this.draftMentions.set([]);
          this.closeMentionPicker();
          this.pendingCommentFiles.set([]);
          this.replyToCommentId.set(null);
          this.loadTaskComments(detail.id);
          this.loadTaskAttachments(detail.id);
          this.attachmentsExpanded.set(true);
          this.loadTaskActivity(detail.id);
        },
        error: (err: HttpErrorResponse) => {
          this.postingComment.set(false);
          this.commentError.set(err.error?.message ?? 'Yorum gönderilemedi.');
        }
      });
  }

  deleteComment(comment: TaskComment): void {
    const detail = this.detail();
    if (!detail || !confirm('Bu yorum ve tüm yanıtları silinsin mi?')) {
      return;
    }

    this.deletingCommentId.set(comment.id);
    this.commentError.set(null);

    this.taskService.deleteComment(detail.id, comment.id).subscribe({
      next: () => {
        this.deletingCommentId.set(null);
        if (this.replyToCommentId() === comment.id) {
          this.replyToCommentId.set(null);
        }
        this.loadTaskComments(detail.id);
        this.loadTaskAttachments(detail.id);
        this.loadTaskActivity(detail.id);
      },
      error: (err: HttpErrorResponse) => {
        this.deletingCommentId.set(null);
        this.commentError.set(err.error?.message ?? 'Yorum silinemedi.');
      }
    });
  }

  downloadCommentAttachment(comment: TaskComment, attachment: CommentAttachment): void {
    const detail = this.detail();
    if (!detail) {
      return;
    }

    this.taskService.downloadCommentAttachment(detail.id, comment.id, attachment.id).subscribe({
      next: (blob) => {
        const url = URL.createObjectURL(blob);
        const anchor = document.createElement('a');
        anchor.href = url;
        anchor.download = attachment.fileName;
        anchor.click();
        URL.revokeObjectURL(url);
      },
      error: (err: HttpErrorResponse) => {
        this.commentError.set(err.error?.message ?? 'Dosya indirilemedi.');
      }
    });
  }

  deleteCommentAttachment(comment: TaskComment, attachment: CommentAttachment): void {
    const detail = this.detail();
    if (!detail || !confirm(`"${attachment.fileName}" dosyası silinsin mi?`)) {
      return;
    }

    const key = `${comment.id}-${attachment.id}`;
    this.deletingCommentAttachmentKey.set(key);
    this.commentError.set(null);

    this.taskService.deleteCommentAttachment(detail.id, comment.id, attachment.id).subscribe({
      next: () => {
        this.deletingCommentAttachmentKey.set(null);
        this.loadTaskComments(detail.id);
        this.loadTaskAttachments(detail.id);
        this.loadTaskActivity(detail.id);
      },
      error: (err: HttpErrorResponse) => {
        this.deletingCommentAttachmentKey.set(null);
        this.commentError.set(err.error?.message ?? 'Dosya silinemedi.');
      }
    });
  }

  commentAuthorName(comment: TaskComment): string {
    const member = this.members().find((m) => m.userId === comment.authorUserId);
    if (member) {
      return this.memberName(member);
    }
    return comment.authorEmail || 'Bir kullanıcı';
  }

  commentAvatarTone(userId: number): string {
    const tones = ['tone-a', 'tone-b', 'tone-c', 'tone-d', 'tone-e', 'tone-f'];
    return tones[Math.abs(userId) % tones.length];
  }

  canDeleteComment(comment: TaskComment): boolean {
    const detail = this.detail();
    const userId = this.user?.userId;
    if (!detail || userId == null) {
      return false;
    }
    return comment.authorUserId === userId || detail.createdByUserId === userId || this.isLeader();
  }

  canDeleteCommentAttachment(comment: TaskComment, attachment: CommentAttachment): boolean {
    const userId = this.user?.userId;
    if (userId == null) {
      return false;
    }
    return (
      attachment.uploadedByUserId === userId ||
      comment.authorUserId === userId ||
      this.canDeleteComment(comment)
    );
  }

  isImageCommentAttachment(attachment: CommentAttachment): boolean {
    return attachment.contentType.startsWith('image/');
  }

  isPdfCommentAttachment(attachment: CommentAttachment): boolean {
    return attachment.contentType === 'application/pdf';
  }

  replyTargetLabel(): string | null {
    const replyId = this.replyToCommentId();
    if (replyId == null) {
      return null;
    }
    const target = this.findCommentById(this.taskComments(), replyId);
    return target ? this.commentAuthorName(target) : null;
  }

  private findCommentById(comments: TaskComment[], commentId: number): TaskComment | null {
    for (const comment of comments) {
      if (comment.id === commentId) {
        return comment;
      }
      const nested = this.findCommentById(comment.replies ?? [], commentId);
      if (nested) {
        return nested;
      }
    }
    return null;
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
    const userId = this.user?.userId;
    if (!detail || userId == null) {
      return false;
    }

    return attachment.uploadedByUserId === userId || detail.createdByUserId === userId || this.isLeader();
  }

  isImageAttachment(attachment: TaskAttachment): boolean {
    if (attachment.contentType.startsWith('image/')) {
      return true;
    }

    return /\.(jpe?g|png|gif|webp)$/i.test(attachment.fileName);
  }

  isPdfAttachment(attachment: TaskAttachment): boolean {
    return (
      attachment.contentType === 'application/pdf' || attachment.fileName.toLowerCase().endsWith('.pdf')
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
    const member = this.members().find((m) => m.userId === log.userId);
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
      case TaskActivityAction.CommentAdded:
        return `${who}, yorum ekledi: "${log.newValue}"`;
      case TaskActivityAction.CommentDeleted:
        return `${who}, bir yorumu sildi: "${log.oldValue}"`;
      default:
        return `${who}, bir işlem yaptı.`;
    }
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
        dueDate: raw.dueDate ? this.toEndOfDayIso(raw.dueDate) : null
      })
      .subscribe({
        next: () => {
          this.savingDetail.set(false);
          this.detailEditing.set(false);
          this.refreshDetail(d.id);
          this.changed.emit();
        },
        error: (err: HttpErrorResponse) => {
          this.savingDetail.set(false);
          this.detailSaveError.set(err.error?.message ?? 'Görev güncellenemedi.');
        }
      });
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
        this.categoryCreated.emit(category);
        this.detailForm.controls.categoryId.setValue(String(category.id));
        this.resetNewCategory();
      },
      error: (err: HttpErrorResponse) => {
        this.creatingCategory.set(false);
        this.newCategoryError.set(
          typeof err.error === 'string' ? err.error : (err.error?.message ?? 'Kategori eklenemedi.')
        );
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
        this.changed.emit();
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
        this.changed.emit();
      },
      error: () => this.refreshDetail(d.id)
    });
  }

  toggleColumnMenu(): void {
    this.showColumnMenu.update((open) => !open);
  }

  closeColumnMenu(): void {
    this.showColumnMenu.set(false);
  }

  selectDetailColumn(column: BoardColumn): void {
    const detail = this.detail();
    if (!detail || detail.boardColumnId === column.id || this.movingColumn()) {
      this.closeColumnMenu();
      return;
    }

    this.movingColumn.set(true);
    this.closeColumnMenu();

    this.taskService.moveToColumn(detail.id, column.id).subscribe({
      next: () => {
        this.movingColumn.set(false);
        this.detail.update((current) =>
          current
            ? {
                ...current,
                boardColumnId: column.id,
                boardColumnTitle: column.title,
                isCompleted: column.isCompletedColumn
              }
            : null
        );
        this.changed.emit();
      },
      error: () => {
        this.movingColumn.set(false);
        this.refreshDetail(detail.id);
      }
    });
  }

  deleteTask(task: TaskDetail): void {
    if (!confirm(`"${task.title}" görevi silinsin mi?`)) {
      return;
    }

    this.taskService.delete(task.id).subscribe({
      next: () => {
        this.changed.emit();
        this.closed.emit();
      },
      error: () => {
        this.changed.emit();
        this.closed.emit();
      }
    });
  }

  private toDateInput(iso: string): string {
    return iso ? iso.substring(0, 10) : '';
  }

  private toEndOfDayIso(dateValue: string): string {
    const date = new Date(dateValue);
    date.setHours(23, 59, 59, 999);
    return date.toISOString();
  }
}
