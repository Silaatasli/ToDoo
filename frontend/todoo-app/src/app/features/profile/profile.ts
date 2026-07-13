import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, DestroyRef, HostListener, inject, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { ImageCroppedEvent, ImageCropperComponent, ImageTransform } from 'ngx-image-cropper';
import { ProfilePhotoCacheService } from '../../core/services/profile-photo-cache.service';
import { UserService } from '../../core/services/user.service';
import { AppLayout } from '../../shared/components/app-layout/app-layout';
import { UserProfile } from '../../models/user.model';

@Component({
  selector: 'app-profile',
  imports: [AppLayout, ReactiveFormsModule, DatePipe, ImageCropperComponent],
  templateUrl: './profile.html',
  styleUrl: './profile.scss'
})
export class Profile implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly userService = inject(UserService);
  private readonly photoCache = inject(ProfilePhotoCacheService);
  private readonly fb = inject(FormBuilder);
  private readonly destroyRef = inject(DestroyRef);

  readonly profile = signal<UserProfile | null>(null);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

  readonly editing = signal(false);
  readonly saving = signal(false);
  readonly saveError = signal<string | null>(null);

  readonly uploadingPhoto = signal(false);
  readonly removingPhoto = signal(false);
  readonly photoError = signal<string | null>(null);

  readonly showCropModal = signal(false);
  readonly imageChangedEvent = signal<Event | null>(null);
  readonly croppedBlob = signal<Blob | null>(null);
  readonly cropReady = signal(false);
  readonly zoomScale = signal(1);
  readonly transform = signal<ImageTransform>({ scale: 1 });

  readonly displayName = computed(() => {
    const p = this.profile();
    if (!p) {
      return '';
    }
    const full = [p.firstName, p.lastName].filter((part) => !!part && part.trim()).join(' ').trim();
    return full || p.email;
  });

  readonly photoUrl = computed(() => {
    const p = this.profile();
    if (!p?.hasProfilePhoto) {
      return null;
    }
    return this.photoCache.photoUrl(p.id);
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
      this.photoError.set(null);
      this.closeCropModal();
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

  @HostListener('document:keydown.escape')
  onEscape(): void {
    if (this.showCropModal() && !this.uploadingPhoto()) {
      this.closeCropModal();
    }
  }

  private loadMine(): void {
    this.loading.set(true);
    this.error.set(null);
    this.userService.getMyProfile().subscribe({
      next: (profile) => {
        this.applyProfile(profile);
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
        this.applyProfile(profile);
        this.loading.set(false);
      },
      error: (err: HttpErrorResponse) => this.handleLoadError(err)
    });
  }

  private applyProfile(profile: UserProfile): void {
    this.profile.set(profile);
    this.photoCache.ensure(profile.id, profile.hasProfilePhoto);
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
          this.applyProfile(profile);
        },
        error: (err: HttpErrorResponse) => {
          this.saving.set(false);
          this.saveError.set(err.error?.message ?? 'Profil güncellenemedi.');
        }
      });
  }

  onPhotoSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file || this.uploadingPhoto()) {
      input.value = '';
      return;
    }

    this.photoError.set(null);
    this.croppedBlob.set(null);
    this.cropReady.set(false);
    this.zoomScale.set(1);
    this.transform.set({ scale: 1 });
    this.imageChangedEvent.set(event);
    this.showCropModal.set(true);
  }

  onImageCropped(event: ImageCroppedEvent): void {
    this.croppedBlob.set(event.blob ?? null);
  }

  onCropperReady(): void {
    this.cropReady.set(true);
  }

  onCropLoadFailed(): void {
    this.photoError.set('Fotoğraf yüklenemedi. JPG, PNG, WEBP veya GIF seçin.');
    this.closeCropModal();
  }

  onZoomChange(event: Event): void {
    const value = Number((event.target as HTMLInputElement).value);
    const scale = Number.isFinite(value) ? value : 1;
    this.zoomScale.set(scale);
    this.transform.update((current) => ({
      ...current,
      scale
    }));
  }

  onTransformChange(transform: ImageTransform): void {
    this.transform.set(transform);
    if (typeof transform.scale === 'number') {
      this.zoomScale.set(transform.scale);
    }
  }

  closeCropModal(): void {
    if (this.uploadingPhoto()) {
      return;
    }
    this.showCropModal.set(false);
    this.imageChangedEvent.set(null);
    this.croppedBlob.set(null);
    this.cropReady.set(false);
    this.zoomScale.set(1);
    this.transform.set({ scale: 1 });
  }

  confirmCroppedPhoto(): void {
    const blob = this.croppedBlob();
    if (!blob || this.uploadingPhoto()) {
      return;
    }

    const file = new File([blob], 'profile-photo.jpg', {
      type: blob.type || 'image/jpeg'
    });

    this.uploadingPhoto.set(true);
    this.photoError.set(null);

    this.userService.uploadMyPhoto(file).subscribe({
      next: (profile) => {
        this.uploadingPhoto.set(false);
        this.closeCropModal();
        this.photoCache.clear(profile.id);
        this.userService.getMyPhoto().subscribe({
          next: (photoBlob) => {
            this.photoCache.setBlob(profile.id, photoBlob);
            this.profile.set(profile);
          },
          error: () => this.applyProfile(profile)
        });
      },
      error: (err: HttpErrorResponse) => {
        this.uploadingPhoto.set(false);
        this.photoError.set(err.error?.message ?? 'Fotoğraf yüklenemedi.');
      }
    });
  }

  removePhoto(): void {
    const p = this.profile();
    if (!p?.hasProfilePhoto || this.removingPhoto()) {
      return;
    }

    if (!confirm('Profil fotoğrafı kaldırılsın mı?')) {
      return;
    }

    this.removingPhoto.set(true);
    this.photoError.set(null);

    this.userService.deleteMyPhoto().subscribe({
      next: (profile) => {
        this.removingPhoto.set(false);
        this.photoCache.clear(profile.id);
        this.profile.set(profile);
      },
      error: (err: HttpErrorResponse) => {
        this.removingPhoto.set(false);
        this.photoError.set(err.error?.message ?? 'Fotoğraf kaldırılamadı.');
      }
    });
  }

  initial(value: string): string {
    return value.trim().charAt(0).toUpperCase() || '?';
  }
}
