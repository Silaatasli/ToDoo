import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, HostListener, computed, inject, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthService } from '../../../core/services/auth.service';
import { CategoryService } from '../../../core/services/category.service';
import { ProfilePhotoCacheService } from '../../../core/services/profile-photo-cache.service';
import { RecentItemsService } from '../../../core/services/recent-items.service';
import { TeamBoardHubService } from '../../../core/services/team-board-hub.service';
import { TaskService } from '../../../core/services/task.service';
import { TeamService } from '../../../core/services/team.service';
import { AppLayout } from '../../../shared/components/app-layout/app-layout';
import { Category } from '../../../models/category.model';
import {
  AssignmentStatus,
  BoardColumnWithTasks,
  BoardListItem,
  Priority,
  TaskListItem,
  TeamBoard as TeamBoardModel,
  TeamDetail
} from '../../../models/team.model';
import { initial, memberName, RemoteTaskDrag } from './board-ui.utils';
import { BoardColumnComponent } from './components/board-column/board-column';
import { CreateBoardModalComponent, CreateBoardPayload } from './components/create-board-modal/create-board-modal';
import { CreateColumnModalComponent, CreateColumnPayload } from './components/create-column-modal/create-column-modal';
import { CreateTaskModalComponent, CreateTaskPayload } from './components/create-task-modal/create-task-modal';
import { MembersModalComponent } from './components/members-modal/members-modal';
import { TaskDetailModalComponent } from './components/task-detail-modal/task-detail-modal';

@Component({
  selector: 'app-team-board',
  imports: [
    AppLayout,
    RouterLink,
    BoardColumnComponent,
    CreateColumnModalComponent,
    CreateBoardModalComponent,
    CreateTaskModalComponent,
    MembersModalComponent,
    TaskDetailModalComponent
  ],
  templateUrl: './team-board.html',
  styleUrl: './team-board.scss'
})
export class TeamBoard implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly teamService = inject(TeamService);
  private readonly taskService = inject(TaskService);
  private readonly categoryService = inject(CategoryService);
  private readonly auth = inject(AuthService);
  private readonly photoCache = inject(ProfilePhotoCacheService);
  private readonly recentItems = inject(RecentItemsService);
  private readonly boardHub = inject(TeamBoardHubService);
  private readonly fb = inject(FormBuilder);
  private readonly destroyRef = inject(DestroyRef);

  private lastRecordedBoardId: number | null = null;

  readonly teamId = signal<number | null>(null);
  readonly boardId = signal<number | null>(null);
  readonly boards = signal<BoardListItem[]>([]);
  readonly board = signal<TeamBoardModel | null>(null);
  readonly team = signal<TeamDetail | null>(null);
  readonly categories = signal<Category[]>([]);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

  readonly showBoardMenu = signal(false);
  readonly showBoardModal = signal(false);
  readonly savingBoard = signal(false);
  readonly boardError = signal<string | null>(null);
  readonly deletingBoard = signal(false);

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

  readonly openTaskId = signal<number | null>(null);

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

  private readonly user = this.auth.getUser();
  private pendingOpenTaskId: number | null = null;

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

  readonly isLeader = computed(() => {
    const t = this.team();
    return !!t && t.leaderUserId === this.user?.userId;
  });

  readonly editColumnForm = this.fb.nonNullable.group({
    title: ['', [Validators.required, Validators.maxLength(100)]]
  });

  readonly initial = initial;
  readonly memberName = memberName;

  readonly memberPhotoUrlFn = (userId: number | null | undefined) => this.memberPhotoUrl(userId);
  readonly taskByIdFn = (taskId: number) => this.taskById(taskId);
  readonly remoteDragLabelFn = (userId: number) => this.remoteDragLabel(userId);

  ngOnInit(): void {
    this.categoryService.getAll().subscribe({
      next: (categories) => this.categories.set(categories),
      error: () => {}
    });

    this.route.paramMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((params) => {
      const id = Number(params.get('id'));
      if (Number.isNaN(id)) {
        this.teamId.set(null);
        this.boardId.set(null);
        this.error.set('Geçersiz takım.');
        this.loading.set(false);
        return;
      }

      this.resetModals();
      this.teamId.set(id);

      const boardIdParam = params.get('boardId');
      if (!boardIdParam) {
        this.redirectToDefaultBoard(id);
        return;
      }

      const boardId = Number(boardIdParam);
      if (Number.isNaN(boardId)) {
        this.boardId.set(null);
        this.error.set('Geçersiz pano.');
        this.loading.set(false);
        return;
      }

      this.boardId.set(boardId);
      this.load();
      void this.connectBoardHub(id);
    });

    this.route.queryParamMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((query) => {
      const taskId = Number(query.get('taskId'));
      if (!Number.isNaN(taskId) && taskId > 0) {
        this.pendingOpenTaskId = taskId;
        this.tryOpenPendingTask();
      }
    });

    this.boardHub.boardChanged$
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((event) => {
        if (event.boardId != null && this.boardId() != null && event.boardId !== this.boardId()) {
          if (event.changeType === 'board-created' || event.changeType === 'board-deleted') {
            this.refreshBoardsList();
          }
          return;
        }

        if (event.actorUserId !== this.user?.userId) {
          this.remoteTaskDrags.set([]);
          this.refreshBoardSilent();
        }

        if (event.changeType === 'board-created' || event.changeType === 'board-deleted') {
          this.refreshBoardsList();
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
  }

  @HostListener('document:click')
  onDocumentClick(): void {
    if (this.showAssigneeFilterMenu()) {
      this.closeAssigneeFilterMenu();
    }

    if (this.showBoardMenu()) {
      this.showBoardMenu.set(false);
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
    this.showBoardMenu.set(false);
    this.showBoardModal.set(false);
    this.showColumnModal.set(false);
    this.showTaskModal.set(false);
    this.showMembersModal.set(false);
    this.openTaskId.set(null);
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

  isAssigneeFilterActive(userId: number): boolean {
    return this.assigneeFilter() === userId;
  }

  isUnassignedFilterActive(): boolean {
    return this.assigneeFilter() === 'unassigned';
  }

  load(): void {
    const id = this.teamId();
    const boardId = this.boardId();
    if (id === null || boardId === null) {
      return;
    }

    this.loading.set(true);
    this.error.set(null);

    this.teamService.getBoard(id, boardId).subscribe({
      next: (board) => {
        this.board.set(board);
        this.loading.set(false);
        if (this.lastRecordedBoardId !== board.boardId) {
          this.lastRecordedBoardId = board.boardId;
          this.recentItems.recordBoard({
            boardId: board.boardId,
            boardName: board.boardName,
            teamId: board.teamId,
            teamName: board.teamName
          });
        }
        this.tryOpenPendingTask();
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

    this.refreshTeamSilent(id);
  }

  /** Used by task-detail modal to refresh board without full-page loading flicker. */
  refreshBoardSilent(): void {
    const id = this.teamId();
    const boardId = this.boardId();
    if (id === null || boardId === null) {
      return;
    }

    this.teamService.getBoard(id, boardId).subscribe({
      next: (board) => {
        this.board.set(board);
        this.tryOpenPendingTask();
      },
      error: () => {}
    });
  }

  private refreshTeamSilent(teamId: number): void {
    this.teamService.getTeam(teamId).subscribe({
      next: (team) => {
        this.team.set(team);
        this.boards.set(team.boards ?? []);
        this.photoCache.ensureMany(
          team.members.map((member) => ({
            userId: member.userId,
            hasProfilePhoto: member.hasProfilePhoto
          }))
        );
      },
      error: () => this.team.set(null)
    });
  }

  private redirectToDefaultBoard(teamId: number): void {
    this.loading.set(true);
    this.teamService.getBoards(teamId).subscribe({
      next: (boards) => {
        const first = boards[0];
        if (!first) {
          this.loading.set(false);
          this.error.set('Bu takımda pano bulunamadı.');
          return;
        }

        void this.router.navigate(['/teams', teamId, 'boards', first.id], {
          replaceUrl: true,
          queryParamsHandling: 'preserve'
        });
      },
      error: (err: HttpErrorResponse) => {
        this.loading.set(false);
        this.error.set(err.error?.message ?? 'Panolar yüklenemedi.');
      }
    });
  }

  private refreshBoardsList(): void {
    const id = this.teamId();
    if (id === null) {
      return;
    }

    this.teamService.getBoards(id).subscribe({
      next: (boards) => this.boards.set(boards),
      error: () => {}
    });
  }

  toggleBoardMenu(): void {
    this.showBoardMenu.update((open) => !open);
  }

  selectBoard(board: BoardListItem): void {
    const teamId = this.teamId();
    if (teamId === null || board.id === this.boardId()) {
      this.showBoardMenu.set(false);
      return;
    }

    this.showBoardMenu.set(false);
    void this.router.navigate(['/teams', teamId, 'boards', board.id]);
  }

  openBoardModal(): void {
    this.boardError.set(null);
    this.showBoardMenu.set(false);
    this.showBoardModal.set(true);
  }

  closeBoardModal(): void {
    if (this.savingBoard()) {
      return;
    }
    this.showBoardModal.set(false);
  }

  submitBoard(payload: CreateBoardPayload): void {
    const teamId = this.teamId();
    if (teamId === null) {
      return;
    }

    this.savingBoard.set(true);
    this.boardError.set(null);

    this.teamService.createBoard(teamId, payload).subscribe({
      next: (board) => {
        this.savingBoard.set(false);
        this.showBoardModal.set(false);
        void this.router.navigate(['/teams', teamId, 'boards', board.id]);
      },
      error: (err: HttpErrorResponse) => {
        this.savingBoard.set(false);
        this.boardError.set(err.error?.message ?? 'Pano oluşturulamadı.');
      }
    });
  }

  deleteCurrentBoard(): void {
    const teamId = this.teamId();
    const boardId = this.boardId();
    if (teamId === null || boardId === null || !this.isLeader() || this.deletingBoard()) {
      return;
    }

    if (this.boards().length <= 1) {
      alert('Takımdaki son pano silinemez.');
      return;
    }

    if (!confirm('Bu panoyu ve içindeki tüm sütun/görevleri silmek istiyor musunuz?')) {
      return;
    }

    this.deletingBoard.set(true);
    this.teamService.deleteBoard(teamId, boardId).subscribe({
      next: () => {
        this.deletingBoard.set(false);
        const remaining = this.boards().filter((board) => board.id !== boardId);
        const next = remaining[0];
        if (next) {
          void this.router.navigate(['/teams', teamId, 'boards', next.id]);
        } else {
          void this.router.navigate(['/teams']);
        }
      },
      error: (err: HttpErrorResponse) => {
        this.deletingBoard.set(false);
        alert(err.error?.message ?? 'Pano silinemedi.');
      }
    });
  }

  // ---- Column ----
  openColumnModal(): void {
    this.columnError.set(null);
    this.showColumnModal.set(true);
  }

  closeColumnModal(): void {
    if (this.savingColumn()) {
      return;
    }
    this.showColumnModal.set(false);
  }

  submitColumn(payload: CreateColumnPayload): void {
    const id = this.teamId();
    const boardId = this.boardId();
    if (id === null || boardId === null) {
      return;
    }

    this.savingColumn.set(true);
    this.columnError.set(null);

    this.teamService.addColumn(id, boardId, payload).subscribe({
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
    const boardId = this.boardId();
    if (id === null || boardId === null || this.editColumnForm.invalid) {
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

    this.teamService.updateColumn(id, boardId, column.id, { title: trimmed }).subscribe({
      next: () => {
        this.savingColumnEdit.set(false);
        this.editingColumnId.set(null);
        const current = this.board();
        if (current) {
          this.board.set({
            ...current,
            columns: current.columns.map((col) =>
              col.id === column.id ? { ...col, title: trimmed } : col
            )
          });
        }
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
    this.taskError.set(null);
    this.showTaskModal.set(true);
  }

  closeTaskModal(): void {
    if (this.savingTask()) {
      return;
    }
    this.showTaskModal.set(false);
  }

  submitTask(payload: CreateTaskPayload): void {
    const id = this.teamId();
    const boardId = this.boardId();
    const columnId = this.targetColumnId();
    if (id === null || boardId === null || columnId === null) {
      return;
    }

    this.savingTask.set(true);
    this.taskError.set(null);

    this.teamService
      .createTask(id, boardId, {
        title: payload.title,
        description: payload.description,
        categoryId: payload.categoryId,
        priority: payload.priority as Priority,
        startDate: new Date().toISOString(),
        dueDate: payload.dueDate ? new Date(payload.dueDate).toISOString() : null,
        boardColumnId: columnId,
        assignedToUserId: payload.assignedToUserId
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
      next: () => this.load(),
      error: () => this.load()
    });
  }

  // ---- Task detail ----
  openTaskDetail(task: TaskListItem): void {
    this.openTaskId.set(task.id);
  }

  private tryOpenPendingTask(): void {
    const taskId = this.pendingOpenTaskId;
    if (taskId == null || this.loading()) {
      return;
    }

    this.pendingOpenTaskId = null;
    this.openTaskId.set(taskId);
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: {},
      replaceUrl: true
    });
  }

  closeTaskDetail(): void {
    this.openTaskId.set(null);
  }

  onCategoryCreated(category: Category): void {
    this.categories.update((list) => [...list, category]);
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
      const boardId = this.boardId();
      if (id === null || boardId === null) {
        return;
      }

      this.teamService.reorderColumns(id, boardId, { columnIds: orderedColumnIds }).subscribe({
        error: () => this.refreshBoardSilent()
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
      error: () => this.refreshBoardSilent()
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
    this.showMembersModal.set(true);
  }

  closeMembersModal(): void {
    this.showMembersModal.set(false);
  }

  reloadTeam(): void {
    const id = this.teamId();
    if (id === null) {
      return;
    }
    this.teamService.getTeam(id).subscribe({
      next: (team) => {
        this.team.set(team);
        this.photoCache.ensureMany(
          team.members.map((member) => ({
            userId: member.userId,
            hasProfilePhoto: member.hasProfilePhoto
          }))
        );
      },
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

  memberPhotoUrl(userId: number | null | undefined): string | null {
    return this.photoCache.photoUrl(userId);
  }
}
