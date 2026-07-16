import { Component, DestroyRef, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { HttpErrorResponse } from '@angular/common/http';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { catchError, debounceTime, distinctUntilChanged, EMPTY, of, switchMap } from 'rxjs';
import { ProfilePhotoCacheService } from '../../../../../core/services/profile-photo-cache.service';
import { TeamService } from '../../../../../core/services/team.service';
import { UserService } from '../../../../../core/services/user.service';
import { TeamMember } from '../../../../../models/team.model';
import { UserSearchResult } from '../../../../../models/user.model';
import { initial, memberName } from '../../board-ui.utils';

@Component({
  selector: 'app-members-modal',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './members-modal.html',
  styleUrl: './members-modal.scss'
})
export class MembersModalComponent {
  private readonly fb = inject(FormBuilder);
  private readonly teamService = inject(TeamService);
  private readonly userService = inject(UserService);
  private readonly photoCache = inject(ProfilePhotoCacheService);
  private readonly destroyRef = inject(DestroyRef);

  readonly teamId = input.required<number>();
  readonly isLeader = input(false);
  readonly members = input<TeamMember[]>([]);

  readonly close = output<void>();
  readonly membersChanged = output<void>();

  readonly savingMember = signal(false);
  readonly memberError = signal<string | null>(null);
  readonly memberSearchResults = signal<UserSearchResult[]>([]);
  readonly memberSearchLoading = signal(false);
  readonly showMemberSearchResults = signal(false);

  readonly initial = initial;
  readonly memberName = memberName;

  readonly memberForm = this.fb.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    searchQuery: ['']
  });

  constructor() {
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
          return this.userService.searchUsers(trimmed).pipe(catchError(() => of<UserSearchResult[]>([])));
        }),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe((results) => {
        this.memberSearchLoading.set(false);
        const existingMemberIds = new Set(this.members().map((member) => member.userId));
        const filtered = results.filter((user) => !existingMemberIds.has(user.id));
        this.memberSearchResults.set(filtered);
        this.photoCache.ensureMany(
          filtered.map((user) => ({ userId: user.id, hasProfilePhoto: user.hasProfilePhoto }))
        );
      });
  }

  memberPhotoUrl(userId: number | null | undefined): string | null {
    return this.photoCache.photoUrl(userId);
  }

  onClose(): void {
    if (this.savingMember()) {
      return;
    }
    this.showMemberSearchResults.set(false);
    this.close.emit();
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
    if (this.memberForm.invalid) {
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
        this.membersChanged.emit();
      },
      error: (err: HttpErrorResponse) => {
        this.savingMember.set(false);
        this.memberError.set(err.error?.message ?? 'Üye eklenemedi.');
      }
    });
  }

  removeMember(userId: number): void {
    const id = this.teamId();

    if (!confirm('Bu üye takımdan çıkarılsın mı?')) {
      return;
    }

    this.teamService.removeMember(id, userId).subscribe({
      next: () => this.membersChanged.emit(),
      error: (err: HttpErrorResponse) => this.memberError.set(err.error?.message ?? 'Üye çıkarılamadı.')
    });
  }
}
