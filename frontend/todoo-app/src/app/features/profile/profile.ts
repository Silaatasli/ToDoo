import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { UserService } from '../../core/services/user.service';
import { AppLayout } from '../../shared/components/app-layout/app-layout';
import { UserProfile } from '../../models/user.model';

@Component({
  selector: 'app-profile',
  imports: [AppLayout, ReactiveFormsModule, DatePipe],
  templateUrl: './profile.html',
  styleUrl: './profile.scss'
})
export class Profile implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly userService = inject(UserService);
  private readonly fb = inject(FormBuilder);
  private readonly destroyRef = inject(DestroyRef);

  readonly profile = signal<UserProfile | null>(null);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

  readonly editing = signal(false);
  readonly saving = signal(false);
  readonly saveError = signal<string | null>(null);

  readonly displayName = computed(() => {
    const p = this.profile();
    if (!p) {
      return '';
    }
    const full = [p.firstName, p.lastName].filter((part) => !!part && part.trim()).join(' ').trim();
    return full || p.email;
  });

  readonly form = this.fb.nonNullable.group({
    firstName: ['', [Validators.maxLength(100)]],
    lastName: ['', [Validators.maxLength(100)]],
    phoneNumber: ['', [Validators.maxLength(30), Validators.pattern(/^\+?[0-9\s\-()]{10,20}$/)]],
    title: ['', [Validators.maxLength(100)]]
  });

  ngOnInit(): void {
    this.route.paramMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((params) => {
      const idParam = params.get('id');
      this.editing.set(false);
      if (idParam) {
        const id = Number(idParam);
        if (Number.isNaN(id)) {
          this.error.set('Geçersiz kullanıcı.');
          this.loading.set(false);
          return;
        }
        this.loadOther(id);
      } else {
        this.loadMine();
      }
    });
  }

  private loadMine(): void {
    this.loading.set(true);
    this.error.set(null);
    this.userService.getMyProfile().subscribe({
      next: (profile) => {
        this.profile.set(profile);
        this.loading.set(false);
      },
      error: (err: HttpErrorResponse) => this.handleLoadError(err)
    });
  }

  private loadOther(id: number): void {
    this.loading.set(true);
    this.error.set(null);
    this.userService.getProfile(id).subscribe({
      next: (profile) => {
        this.profile.set(profile);
        this.loading.set(false);
      },
      error: (err: HttpErrorResponse) => this.handleLoadError(err)
    });
  }

  private handleLoadError(err: HttpErrorResponse): void {
    this.loading.set(false);
    this.error.set(
      err.status === 0
        ? "API'ye bağlanılamadı. Backend çalışıyor mu?"
        : (err.error?.message ?? 'Profil yüklenemedi.')
    );
  }

  startEdit(): void {
    const p = this.profile();
    if (!p) {
      return;
    }
    this.saveError.set(null);
    this.form.setValue({
      firstName: p.firstName ?? '',
      lastName: p.lastName ?? '',
      phoneNumber: p.phoneNumber ?? '',
      title: p.title ?? ''
    });
    this.editing.set(true);
  }

  cancelEdit(): void {
    if (this.saving()) {
      return;
    }
    this.editing.set(false);
    this.saveError.set(null);
  }

  save(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.saving.set(true);
    this.saveError.set(null);

    const raw = this.form.getRawValue();
    this.userService
      .updateMyProfile({
        firstName: raw.firstName.trim() || null,
        lastName: raw.lastName.trim() || null,
        phoneNumber: raw.phoneNumber.trim() || null,
        title: raw.title.trim() || null
      })
      .subscribe({
        next: (profile) => {
          this.saving.set(false);
          this.editing.set(false);
          this.profile.set(profile);
        },
        error: (err: HttpErrorResponse) => {
          this.saving.set(false);
          this.saveError.set(err.error?.message ?? 'Profil güncellenemedi.');
        }
      });
  }

  initial(value: string): string {
    return value.trim().charAt(0).toUpperCase() || '?';
  }
}
