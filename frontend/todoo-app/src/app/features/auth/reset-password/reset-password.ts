import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthService } from '../../../core/services/auth.service';

@Component({
  selector: 'app-reset-password',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './reset-password.html',
  styleUrl: './reset-password.scss'
})
export class ResetPassword {
  private readonly fb = inject(FormBuilder);
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  readonly error = signal<string | null>(null);
  readonly success = signal<string | null>(null);
  readonly loading = signal(false);
  readonly token = signal(this.route.snapshot.queryParamMap.get('token') ?? '');

  readonly form = this.fb.nonNullable.group({
    newPassword: ['', [Validators.required, Validators.minLength(6)]],
    confirmPassword: ['', [Validators.required, Validators.minLength(6)]]
  });

  submit(): void {
    if (!this.token()) {
      this.error.set('Şifre sıfırlama linki geçersiz.');
      return;
    }

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const { newPassword, confirmPassword } = this.form.getRawValue();
    if (newPassword !== confirmPassword) {
      this.error.set('Şifreler eşleşmiyor.');
      return;
    }

    this.loading.set(true);
    this.error.set(null);
    this.success.set(null);

    this.auth.resetPassword(this.token(), newPassword).subscribe({
      next: (result) => {
        this.loading.set(false);
        if (result.success) {
          this.success.set(result.message);
          setTimeout(() => void this.router.navigate(['/login']), 1500);
          return;
        }

        this.error.set(result.message);
      },
      error: (err: HttpErrorResponse) => {
        this.loading.set(false);

        if (err.status === 0) {
          this.error.set("API'ye baglanilamadi.");
          return;
        }

        this.error.set(err.error?.message ?? 'Şifre guncellenemedi.');
      }
    });
  }
}
