import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormArray, FormBuilder, FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { catchError, concatMap, debounceTime, distinctUntilChanged, from, of, switchMap, toArray } from 'rxjs';
import { AuthService } from '../../../core/services/auth.service';
import { TeamService } from '../../../core/services/team.service';
import { UserService } from '../../../core/services/user.service';
import { AppLayout } from '../../../shared/components/app-layout/app-layout';
import { TeamDetail, TeamListItem } from '../../../models/team.model';
import { UserSearchResult } from '../../../models/user.model';

interface PendingMember {
  id: number;
  email: string;
  displayName: string;
}

@Component({
  selector: 'app-team-list',
  imports: [AppLayout, ReactiveFormsModule, RouterLink],
  templateUrl: './team-list.html',
  styleUrl: './team-list.scss'
})
export class TeamList implements OnInit {
  private readonly teamService = inject(TeamService);
  private readonly userService = inject(UserService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly fb = inject(FormBuilder);
  private readonly destroyRef = inject(DestroyRef);

  readonly teams = signal<TeamListItem[]>([]);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly showCreateModal = signal(false);
  readonly createStep = signal<1 | 2>(1);
  readonly creating = signal(false);
  readonly createError = signal<string | null>(null);

  readonly pendingMembers = signal<PendingMember[]>([]);
  readonly memberSearchResults = signal<UserSearchResult[]>([]);
  readonly memberSearchLoading = signal(false);
  readonly showMemberSearchResults = signal(false);
  readonly memberError = signal<string | null>(null);

  readonly user = this.auth.getUser();

  private readonly defaultColumns = ['All Tasks', 'In Progress', 'Completed'];
  private readonly defaultBoardName = 'Ana pano';

  readonly createForm = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(200)]],
    boardName: [this.defaultBoardName, [Validators.required, Validators.maxLength(200)]],
    columns: this.fb.array(this.defaultColumns.map((title) => this.buildColumnControl(title)))
  });

  readonly memberSearchControl = this.fb.nonNullable.control('');

  get columns(): FormArray<FormControl<string>> {
    return this.createForm.controls.columns;
  }

  private buildColumnControl(value = ''): FormControl<string> {
    return this.fb.nonNullable.control(value, [Validators.maxLength(100)]);
  }

  addColumnField(): void {
    this.columns.push(this.buildColumnControl());
  }

  removeColumnField(index: number): void {
    this.columns.removeAt(index);
  }

  private resetColumns(): void {
    this.columns.clear();
    this.defaultColumns.forEach((title) => this.columns.push(this.buildColumnControl(title)));
  }

  ngOnInit(): void {
    this.loadTeams();

    this.memberSearchControl.valueChanges
      .pipe(
        debounceTime(250),
        distinctUntilChanged(),
        switchMap((raw) => {
          const query = raw.trim();
          this.memberError.set(null);
          if (query.length < 3) {
            this.memberSearchResults.set([]);
            this.memberSearchLoading.set(false);
            return of<UserSearchResult[]>([]);
          }

          this.memberSearchLoading.set(true);
          return this.userService.searchUsers(query).pipe(
            catchError(() => of<UserSearchResult[]>([]))
          );
        }),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe((results) => {
        this.memberSearchLoading.set(false);
        const pendingIds = new Set(this.pendingMembers().map((member) => member.id));
        const selfId = this.user?.userId;
        this.memberSearchResults.set(
          results.filter((user) => user.id !== selfId && !pendingIds.has(user.id))
        );
      });
  }

  loadTeams(): void {
    this.loading.set(true);
    this.error.set(null);

    this.teamService.getTeams().subscribe({
      next: (teams) => {
        this.teams.set(teams);
        this.loading.set(false);
      },
      error: (err: HttpErrorResponse) => {
        this.loading.set(false);
        this.error.set(
          err.status === 0
            ? "API'ye bağlanılamadı. Backend çalışıyor mu?"
            : (err.error?.message ?? 'Takımlar yüklenemedi.')
        );
      }
    });
  }

  openCreateModal(): void {
    this.createForm.reset({ name: '', boardName: this.defaultBoardName });
    this.resetColumns();
    this.createStep.set(1);
    this.pendingMembers.set([]);
    this.memberSearchControl.setValue('', { emitEvent: false });
    this.memberSearchResults.set([]);
    this.showMemberSearchResults.set(false);
    this.memberError.set(null);
    this.createError.set(null);
    this.showCreateModal.set(true);
  }

  closeCreateModal(): void {
    if (this.creating()) {
      return;
    }

    this.showCreateModal.set(false);
    this.createError.set(null);
    this.memberError.set(null);
  }

  goToMembersStep(): void {
    if (this.createForm.invalid) {
      this.createForm.markAllAsTouched();
      return;
    }

    this.createError.set(null);
    this.createStep.set(2);
  }

  backToBoardStep(): void {
    if (this.creating()) {
      return;
    }

    this.createStep.set(1);
    this.createError.set(null);
    this.memberError.set(null);
  }

  onMemberSearchFocus(): void {
    this.showMemberSearchResults.set(true);
  }

  onMemberSearchInput(): void {
    this.showMemberSearchResults.set(true);
  }

  selectMemberFromSearch(user: UserSearchResult): void {
    if (this.pendingMembers().some((member) => member.id === user.id)) {
      this.memberError.set('Bu kullanıcı zaten listede.');
      return;
    }

    this.pendingMembers.update((members) => [
      ...members,
      { id: user.id, email: user.email, displayName: user.displayName }
    ]);
    this.memberSearchControl.setValue('', { emitEvent: false });
    this.memberSearchResults.set([]);
    this.showMemberSearchResults.set(false);
    this.memberError.set(null);
  }

  removePendingMember(userId: number): void {
    this.pendingMembers.update((members) => members.filter((member) => member.id !== userId));
  }

  submitCreate(): void {
    if (this.createForm.invalid) {
      this.createForm.markAllAsTouched();
      this.createStep.set(1);
      return;
    }

    this.creating.set(true);
    this.createError.set(null);

    const { name, boardName, columns } = this.createForm.getRawValue();
    const columnTitles = columns.map((title) => title.trim()).filter((title) => title.length > 0);
    const trimmedBoardName = boardName.trim();
    const memberEmails = this.pendingMembers().map((member) => member.email);

    this.teamService
      .createTeam({
        name: name.trim(),
        boardName: trimmedBoardName.length > 0 ? trimmedBoardName : undefined,
        columnTitles: columnTitles.length > 0 ? columnTitles : undefined
      })
      .pipe(
        switchMap((team) => {
          if (memberEmails.length === 0) {
            return of({ team, memberErrors: [] as string[] });
          }

          return from(memberEmails).pipe(
            concatMap((email) =>
              this.teamService.addMember(team.id, { email }).pipe(
                catchError((err: HttpErrorResponse) =>
                  of({ error: err.error?.message ?? 'Üye eklenemedi.', email })
                )
              )
            ),
            toArray(),
            switchMap((results) => {
              const memberErrors = results
                .filter((result): result is { error: string; email: string } =>
                  !!result && typeof result === 'object' && 'error' in result && 'email' in result
                )
                .map((result) => `${result.email}: ${result.error}`);
              return of({ team, memberErrors });
            })
          );
        })
      )
      .subscribe({
        next: ({ team, memberErrors }) => {
          this.creating.set(false);
          this.showCreateModal.set(false);
          this.loadTeams();
          this.navigateToFirstBoard(team);

          if (memberErrors.length > 0) {
            // Takım oluştu; üye hatalarını alert ile bildir.
            alert(`Takım oluşturuldu, bazı üyeler eklenemedi:\n${memberErrors.join('\n')}`);
          }
        },
        error: (err: HttpErrorResponse) => {
          this.creating.set(false);
          this.createError.set(
            err.status === 0
              ? "API'ye bağlanılamadı. Backend çalışıyor mu?"
              : (err.error?.message ?? 'Takım oluşturulamadı.')
          );
        }
      });
  }

  private navigateToFirstBoard(team: TeamDetail): void {
    const firstBoardId = team.boards?.[0]?.id;
    if (firstBoardId) {
      void this.router.navigate(['/teams', team.id, 'boards', firstBoardId]);
    } else {
      void this.router.navigate(['/teams', team.id, 'board']);
    }
  }

  isLeader(team: TeamListItem): boolean {
    return team.leaderUserId === this.user?.userId;
  }

  teamInitial(name: string): string {
    return name.trim().charAt(0).toUpperCase() || '?';
  }
}
