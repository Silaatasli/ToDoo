import {
  CdkDrag,
  CdkDragDrop,
  CdkDropList,
  CdkDropListGroup,
  moveItemInArray,
  transferArrayItem
} from '@angular/cdk/drag-drop';
import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { AuthService } from '../../../core/services/auth.service';
import { CategoryService } from '../../../core/services/category.service';
import { ProfilePhotoCacheService } from '../../../core/services/profile-photo-cache.service';
import { SprintService } from '../../../core/services/sprint.service';
import { TeamService } from '../../../core/services/team.service';
import { Category } from '../../../models/category.model';
import {
  BoardKapsam,
  SprintDetail,
  SprintStatus,
  SprintTask
} from '../../../models/sprint.model';
import {
  BoardColumn,
  BoardListItem,
  TeamBoard,
  TeamDetail
} from '../../../models/team.model';
import { AppLayout } from '../../../shared/components/app-layout/app-layout';
import { TeamWorkspaceShell } from '../../../shared/components/team-workspace-shell/team-workspace-shell';
import { TaskDetailModalComponent } from '../team-board/components/task-detail-modal/task-detail-modal';

type DropListId = 'backlog' | `sprint-${number}`;

@Component({
  selector: 'app-team-kapsam',
  imports: [
    AppLayout,
    TeamWorkspaceShell,
    CdkDropListGroup,
    CdkDropList,
    CdkDrag,
    ReactiveFormsModule,
    DatePipe,
    TaskDetailModalComponent
  ],
  templateUrl: './team-kapsam.html',
  styleUrl: './team-kapsam.scss'
})
export class TeamKapsamPage implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly teamService = inject(TeamService);
  private readonly sprintService = inject(SprintService);
  private readonly categoryService = inject(CategoryService);
  private readonly auth = inject(AuthService);
  private readonly photoCache = inject(ProfilePhotoCacheService);
  private readonly fb = inject(FormBuilder);
  private readonly destroyRef = inject(DestroyRef);

  readonly teamId = signal<number | null>(null);
  readonly boardId = signal<number | null>(null);
  readonly team = signal<TeamDetail | null>(null);
  readonly boards = signal<BoardListItem[]>([]);
  readonly board = signal<TeamBoard | null>(null);
  readonly categories = signal<Category[]>([]);
  readonly kapsam = signal<BoardKapsam | null>(null);
  /** CDK mutable listeler — signal degil, ayni referans korunmali. */
  backlogList: SprintTask[] = [];
  sprintLists: Record<number, SprintTask[]> = {};
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly actionError = signal<string | null>(null);
  readonly busy = signal(false);

  readonly backlogExpanded = signal(true);
  readonly expandedSprintIds = signal<Set<number>>(new Set());

  readonly showCreateModal = signal(false);
  readonly showEditModal = signal(false);
  readonly editingSprint = signal<SprintDetail | null>(null);
  readonly showCompleteModal = signal(false);
  readonly showCancelModal = signal(false);
  readonly showStartBlockedModal = signal(false);
  readonly startBlockedActiveName = signal<string | null>(null);
  readonly completingSprint = signal<SprintDetail | null>(null);
  readonly cancellingSprint = signal<SprintDetail | null>(null);
  readonly completeDestination = signal<'backlog' | 'sprint'>('backlog');
  readonly cancelDestination = signal<'backlog' | 'sprint'>('backlog');
  readonly completeTargetSprintId = signal<number | null>(null);
  readonly cancelTargetSprintId = signal<number | null>(null);

  readonly openTaskId = signal<number | null>(null);
  private taskDragMoved = false;

  readonly sprintStatus = SprintStatus;

  readonly createForm = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(200)]],
    goal: [''],
    plannedStartDate: ['', Validators.required],
    plannedEndDate: ['', Validators.required]
  });

  readonly editForm = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(200)]],
    goal: [''],
    plannedStartDate: ['', Validators.required],
    plannedEndDate: ['', Validators.required]
  });

  readonly plannedSprints = computed(() =>
    (this.kapsam()?.sprints ?? []).filter((s) => Number(s.status) === SprintStatus.Planned)
  );

  readonly activeSprint = computed(
    () =>
      (this.kapsam()?.sprints ?? []).find((s) => Number(s.status) === SprintStatus.Active) ?? null
  );

  readonly visibleSprints = computed(() =>
    (this.kapsam()?.sprints ?? []).filter((s) => Number(s.status) !== SprintStatus.Cancelled)
  );

  readonly completeIncompleteCount = computed(() => {
    const sprint = this.completingSprint();
    if (!sprint) {
      return 0;
    }
    return this.tasksForSprint(sprint.id).filter((task) => !task.isCompleted).length;
  });

  readonly isLeader = computed(() => {
    const team = this.team();
    const userId = this.auth.getUserId();
    if (!team || userId == null) {
      return false;
    }
    return team.members.some((member) => member.userId === userId && member.isLeader);
  });

  readonly boardColumns = computed<BoardColumn[]>(() => this.board()?.columns ?? []);

  readonly memberPhotoUrlFn = (userId: number | null | undefined) =>
    userId == null ? null : this.photoCache.photoUrl(userId);

  targetSprintsFor(excludeId: number): SprintDetail[] {
    return this.plannedSprints().filter((item) => item.id !== excludeId);
  }

  ngOnInit(): void {
    this.categoryService.getAll().subscribe({
      next: (categories) => this.categories.set(categories),
      error: () => this.categories.set([])
    });

    this.route.paramMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((params) => {
      const id = Number(params.get('id'));
      if (!Number.isFinite(id) || id <= 0) {
        this.error.set('Geçersiz takım.');
        this.loading.set(false);
        return;
      }

      this.teamId.set(id);
      this.bootstrap(id);
    });
  }

  selectBoard(boardId: number): void {
    this.boardId.set(boardId);
    this.closeTaskDetail();
    this.loadBoardMeta(boardId);
    this.loadKapsam();
  }

  toggleBacklog(): void {
    this.backlogExpanded.update((open) => !open);
  }

  toggleSprint(sprintId: number): void {
    this.expandedSprintIds.update((set) => {
      const next = new Set(set);
      if (next.has(sprintId)) {
        next.delete(sprintId);
      } else {
        next.add(sprintId);
      }
      return next;
    });
  }

  isSprintExpanded(sprintId: number): boolean {
    return this.expandedSprintIds().has(sprintId);
  }

  tasksForSprint(sprintId: number): SprintTask[] {
    if (!this.sprintLists[sprintId]) {
      this.sprintLists[sprintId] = [];
    }
    return this.sprintLists[sprintId];
  }

  isPlanned(sprint: SprintDetail): boolean {
    return Number(sprint.status) === SprintStatus.Planned;
  }

  isActive(sprint: SprintDetail): boolean {
    return Number(sprint.status) === SprintStatus.Active;
  }

  isCompleted(sprint: SprintDetail): boolean {
    return Number(sprint.status) === SprintStatus.Completed;
  }

  canEditSprint(sprint: SprintDetail): boolean {
    return this.isPlanned(sprint) || this.isActive(sprint);
  }

  statusLabel(status: SprintStatus): string {
    switch (status) {
      case SprintStatus.Active:
        return 'Aktif';
      case SprintStatus.Completed:
        return 'Tamamlandı';
      case SprintStatus.Cancelled:
        return 'İptal';
      default:
        return 'Planlandı';
    }
  }

  openCreateModal(): void {
    const start = new Date();
    const end = new Date();
    end.setDate(end.getDate() + 14);
    this.createForm.reset({
      name: '',
      goal: '',
      plannedStartDate: start.toISOString().slice(0, 10),
      plannedEndDate: end.toISOString().slice(0, 10)
    });
    this.actionError.set(null);
    this.showCreateModal.set(true);
  }

  closeCreateModal(): void {
    this.showCreateModal.set(false);
  }

  submitCreate(): void {
    const teamId = this.teamId();
    const boardId = this.boardId();
    if (!teamId || !boardId || this.createForm.invalid || this.busy()) {
      this.createForm.markAllAsTouched();
      return;
    }

    const value = this.createForm.getRawValue();
    this.busy.set(true);
    this.actionError.set(null);
    this.sprintService
      .createSprint(teamId, boardId, {
        name: value.name.trim(),
        goal: value.goal.trim() || null,
        plannedStartDate: new Date(value.plannedStartDate).toISOString(),
        plannedEndDate: new Date(value.plannedEndDate + 'T23:59:59').toISOString()
      })
      .subscribe({
        next: (sprint) => {
          this.busy.set(false);
          this.showCreateModal.set(false);
          this.expandedSprintIds.update((set) => new Set(set).add(sprint.id));
          this.loadKapsam(true);
        },
        error: (err: HttpErrorResponse) => {
          this.busy.set(false);
          this.actionError.set(err.error?.message ?? 'Sprint oluşturulamadı.');
        }
      });
  }

  openEditModal(sprint: SprintDetail, event?: Event): void {
    event?.stopPropagation();
    if (!this.canEditSprint(sprint)) {
      return;
    }

    this.editingSprint.set(sprint);
    this.editForm.reset({
      name: sprint.name,
      goal: sprint.goal ?? '',
      plannedStartDate: sprint.plannedStartDate.slice(0, 10),
      plannedEndDate: sprint.plannedEndDate.slice(0, 10)
    });
    this.actionError.set(null);
    this.showEditModal.set(true);
  }

  closeEditModal(): void {
    this.showEditModal.set(false);
    this.editingSprint.set(null);
  }

  submitEdit(): void {
    const sprint = this.editingSprint();
    if (!sprint || this.editForm.invalid || this.busy()) {
      this.editForm.markAllAsTouched();
      return;
    }

    const value = this.editForm.getRawValue();
    this.busy.set(true);
    this.actionError.set(null);
    this.sprintService
      .updateSprint(sprint.id, {
        name: value.name.trim(),
        goal: value.goal.trim() || null,
        plannedStartDate: new Date(value.plannedStartDate).toISOString(),
        plannedEndDate: new Date(value.plannedEndDate + 'T23:59:59').toISOString()
      })
      .subscribe({
        next: () => {
          this.busy.set(false);
          this.closeEditModal();
          this.loadKapsam(true);
        },
        error: (err: HttpErrorResponse) => {
          this.busy.set(false);
          this.actionError.set(err.error?.message ?? 'Sprint güncellenemedi.');
        }
      });
  }

  startSprint(sprint: SprintDetail, event?: Event): void {
    event?.stopPropagation();
    if (this.busy() || this.tasksForSprint(sprint.id).length === 0) {
      return;
    }

    const active = this.activeSprint();
    if (active && active.id !== sprint.id) {
      this.startBlockedActiveName.set(active.name);
      this.showStartBlockedModal.set(true);
      return;
    }

    this.busy.set(true);
    this.actionError.set(null);
    this.sprintService.startSprint(sprint.id).subscribe({
      next: () => {
        this.busy.set(false);
        this.loadKapsam();
      },
      error: (err: HttpErrorResponse) => {
        this.busy.set(false);
        const message = err.error?.message ?? 'Sprint başlatılamadı.';
        if (/aktif bir sprint/i.test(message)) {
          this.startBlockedActiveName.set(this.activeSprint()?.name ?? null);
          this.showStartBlockedModal.set(true);
          this.actionError.set(null);
          return;
        }
        this.actionError.set(message);
      }
    });
  }

  closeStartBlockedModal(): void {
    this.showStartBlockedModal.set(false);
    this.startBlockedActiveName.set(null);
  }

  openCompleteModal(sprint: SprintDetail, event?: Event): void {
    event?.stopPropagation();
    this.completingSprint.set(sprint);
    this.completeDestination.set('backlog');
    const targets = this.targetSprintsFor(sprint.id);
    this.completeTargetSprintId.set(targets[0]?.id ?? null);
    this.actionError.set(null);
    this.showCompleteModal.set(true);
  }

  closeCompleteModal(): void {
    this.showCompleteModal.set(false);
    this.completingSprint.set(null);
  }

  setCompleteDestination(destination: 'backlog' | 'sprint'): void {
    this.completeDestination.set(destination);
    if (destination === 'sprint') {
      const sprint = this.completingSprint();
      const targets = sprint ? this.targetSprintsFor(sprint.id) : [];
      if (!this.completeTargetSprintId() && targets[0]) {
        this.completeTargetSprintId.set(targets[0].id);
      }
    }
  }

  onCompleteTargetChange(event: Event): void {
    const raw = (event.target as HTMLSelectElement).value;
    this.completeTargetSprintId.set(raw ? Number(raw) : null);
  }

  submitComplete(): void {
    const sprint = this.completingSprint();
    if (!sprint || this.busy()) {
      return;
    }

    const incomplete = this.completeIncompleteCount();
    let destination = this.completeDestination();
    let targetSprintId = this.completeTargetSprintId();

    if (incomplete === 0) {
      destination = 'backlog';
      targetSprintId = null;
    } else if (destination === 'sprint') {
      if (!targetSprintId) {
        this.actionError.set('Hedef sprint seçin.');
        return;
      }
      if (this.targetSprintsFor(sprint.id).length === 0) {
        this.actionError.set('Taşımak için önce planlanmış başka bir sprint oluşturun.');
        return;
      }
    }

    this.busy.set(true);
    this.actionError.set(null);
    this.sprintService
      .completeSprint(sprint.id, {
        incompleteDestination: destination,
        targetSprintId: destination === 'sprint' ? targetSprintId : null
      })
      .subscribe({
        next: () => {
          this.busy.set(false);
          this.closeCompleteModal();
          this.loadKapsam(true);
        },
        error: (err: HttpErrorResponse) => {
          this.busy.set(false);
          this.actionError.set(err.error?.message ?? 'Sprint tamamlanamadı.');
        }
      });
  }

  openCancelModal(sprint: SprintDetail, event?: Event): void {
    event?.stopPropagation();
    this.cancellingSprint.set(sprint);
    this.cancelDestination.set('backlog');
    const targets = this.targetSprintsFor(sprint.id);
    this.cancelTargetSprintId.set(targets[0]?.id ?? null);
    this.actionError.set(null);
    this.showCancelModal.set(true);
  }

  closeCancelModal(): void {
    this.showCancelModal.set(false);
    this.cancellingSprint.set(null);
  }

  setCancelDestination(destination: 'backlog' | 'sprint'): void {
    this.cancelDestination.set(destination);
    if (destination === 'sprint') {
      const sprint = this.cancellingSprint();
      const targets = sprint ? this.targetSprintsFor(sprint.id) : [];
      if (!this.cancelTargetSprintId() && targets[0]) {
        this.cancelTargetSprintId.set(targets[0].id);
      }
    }
  }

  onCancelTargetChange(event: Event): void {
    const raw = (event.target as HTMLSelectElement).value;
    this.cancelTargetSprintId.set(raw ? Number(raw) : null);
  }

  submitCancel(): void {
    const sprint = this.cancellingSprint();
    if (!sprint || this.busy()) {
      return;
    }

    const destination = this.cancelDestination();
    const targetSprintId = this.cancelTargetSprintId();
    if (destination === 'sprint') {
      if (!targetSprintId) {
        this.actionError.set('Hedef sprint seçin.');
        return;
      }
      if (this.targetSprintsFor(sprint.id).length === 0) {
        this.actionError.set('Taşımak için önce planlanmış başka bir sprint oluşturun.');
        return;
      }
    }

    this.busy.set(true);
    this.actionError.set(null);
    this.sprintService
      .cancelSprint(sprint.id, {
        taskDestination: destination,
        targetSprintId: destination === 'sprint' ? targetSprintId : null
      })
      .subscribe({
        next: () => {
          this.busy.set(false);
          this.closeCancelModal();
          this.loadKapsam(true);
        },
        error: (err: HttpErrorResponse) => {
          this.busy.set(false);
          this.actionError.set(err.error?.message ?? 'Sprint iptal edilemedi.');
        }
      });
  }

  deleteSprint(sprint: SprintDetail, event?: Event): void {
    event?.stopPropagation();
    if (sprint.status !== SprintStatus.Planned || this.busy()) {
      return;
    }

    if (!confirm(`"${sprint.name}" sprintini silmek istiyor musunuz? Görevler backlog'a taşınır.`)) {
      return;
    }

    this.busy.set(true);
    this.sprintService.deleteSprint(sprint.id).subscribe({
      next: () => {
        this.busy.set(false);
        this.loadKapsam();
      },
      error: (err: HttpErrorResponse) => {
        this.busy.set(false);
        this.actionError.set(err.error?.message ?? 'Sprint silinemedi.');
      }
    });
  }

  onTaskDragStarted(): void {
    this.taskDragMoved = false;
  }

  onTaskDragMoved(): void {
    this.taskDragMoved = true;
  }

  onTaskDragEnded(): void {
    // Drop sonrasinda gelen sahte click'i engelle.
    if (!this.taskDragMoved) {
      return;
    }
    setTimeout(() => {
      this.taskDragMoved = false;
    }, 0);
  }

  openTaskDetail(task: SprintTask, event?: Event): void {
    event?.stopPropagation();
    if (this.taskDragMoved || this.busy()) {
      return;
    }
    this.openTaskId.set(task.id);
  }

  closeTaskDetail(): void {
    this.openTaskId.set(null);
  }

  onTaskDetailChanged(): void {
    this.loadKapsam(true);
  }

  onCategoryCreated(category: Category): void {
    this.categories.update((list) => [...list, category]);
  }

  onDrop(event: CdkDragDrop<SprintTask[]>, target: DropListId): void {
    if (event.previousContainer === event.container) {
      moveItemInArray(event.container.data, event.previousIndex, event.currentIndex);
      this.persistReorder(target, event.container.data);
      return;
    }

    transferArrayItem(
      event.previousContainer.data,
      event.container.data,
      event.previousIndex,
      event.currentIndex
    );

    const task = event.container.data[event.currentIndex];
    if (!task) {
      this.loadKapsam();
      return;
    }

    this.busy.set(true);
    this.actionError.set(null);

    if (target === 'backlog') {
      this.sprintService.moveTaskToBacklog(task.id, event.currentIndex).subscribe({
        next: () => {
          this.busy.set(false);
          this.loadKapsam(true);
        },
        error: (err: HttpErrorResponse) => {
          this.busy.set(false);
          this.actionError.set(err.error?.message ?? 'Görev taşınamadı.');
          this.loadKapsam(true);
        }
      });
      return;
    }

    this.sprintService
      .moveTaskToSprint(Number(target.replace('sprint-', '')), task.id, event.currentIndex)
      .subscribe({
        next: () => {
          this.busy.set(false);
          this.loadKapsam(true);
        },
        error: (err: HttpErrorResponse) => {
          this.busy.set(false);
          this.actionError.set(err.error?.message ?? 'Görev taşınamadı.');
          this.loadKapsam(true);
        }
      });
  }

  private persistReorder(target: DropListId, tasks: SprintTask[]): void {
    const teamId = this.teamId();
    const boardId = this.boardId();
    const taskIds = tasks.map((task) => task.id);
    this.busy.set(true);

    const onError = (err: HttpErrorResponse) => {
      this.busy.set(false);
      this.actionError.set(err.error?.message ?? 'Sıralama kaydedilemedi.');
      this.loadKapsam();
    };

    if (target === 'backlog') {
      if (!teamId || !boardId) {
        this.busy.set(false);
        return;
      }
      this.sprintService.reorderBacklog(teamId, boardId, taskIds).subscribe({
        next: () => this.busy.set(false),
        error: onError
      });
      return;
    }

    this.sprintService.reorderSprintTasks(Number(target.replace('sprint-', '')), taskIds).subscribe({
      next: () => this.busy.set(false),
      error: onError
    });
  }

  private bootstrap(teamId: number): void {
    this.loading.set(true);
    this.error.set(null);
    this.teamService.getTeam(teamId).subscribe({
      next: (team) => {
        this.team.set(team);
        this.photoCache.ensureMany(
          team.members.map((member) => ({
            userId: member.userId,
            hasProfilePhoto: member.hasProfilePhoto
          }))
        );
        this.teamService.getBoards(teamId).subscribe({
          next: (boards) => {
            this.boards.set(boards);
            const first = boards[0];
            if (!first) {
              this.error.set('Bu takımda pano yok.');
              this.loading.set(false);
              return;
            }
            this.boardId.set(first.id);
            this.loadBoardMeta(first.id);
            this.loadKapsam();
          },
          error: (err: HttpErrorResponse) => {
            this.error.set(err.error?.message ?? 'Panolar yüklenemedi.');
            this.loading.set(false);
          }
        });
      },
      error: (err: HttpErrorResponse) => {
        this.error.set(err.error?.message ?? 'Takım yüklenemedi.');
        this.loading.set(false);
      }
    });
  }

  private loadBoardMeta(boardId: number): void {
    const teamId = this.teamId();
    if (!teamId) {
      return;
    }

    this.teamService.getBoard(teamId, boardId).subscribe({
      next: (board) => this.board.set(board),
      error: () => this.board.set(null)
    });
  }

  private loadKapsam(silent = false): void {
    const teamId = this.teamId();
    const boardId = this.boardId();
    if (!teamId || !boardId) {
      return;
    }

    if (!silent) {
      this.loading.set(true);
    }
    this.sprintService.getKapsam(teamId, boardId).subscribe({
      next: (data) => {
        this.kapsam.set(data);
        this.backlogList = [...(data.backlogTasks ?? [])];
        const map: Record<number, SprintTask[]> = {};
        for (const sprint of data.sprints) {
          map[sprint.id] = [...(sprint.tasks ?? [])];
        }
        this.sprintLists = map;

        if (!silent) {
          const openIds = data.sprints
            .filter((s) => s.status === SprintStatus.Active || s.status === SprintStatus.Planned)
            .map((s) => s.id);
          this.expandedSprintIds.set(new Set(openIds));
          this.backlogExpanded.set(true);
        }
        this.loading.set(false);
      },
      error: (err: HttpErrorResponse) => {
        this.error.set(err.error?.message ?? 'Kapsam yüklenemedi.');
        this.loading.set(false);
      }
    });
  }
}
