import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../../core/services/auth.service';

@Component({
  selector: 'app-forgot-password',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './forgot-password.html',
  styleUrl: './forgot-password.scss'
})
export class ForgotPassword {
  private readonly fb = inject(FormBuilder);
  private readonly auth = inject(AuthService);

  readonly error = signal<string | null>(null);
  readonly success = signal<string | null>(null);
  readonly loading = signal(false);

  readonly form = this.fb.nonNullable.group({
    email: ['', [Validators.required, Validators.email]]
  });

  submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.loading.set(true);
    this.error.set(null);
    this.success.set(null);

    const { email } = this.form.getRawValue();

    this.auth.forgotPassword(email).subscribe({
      next: (result) => {
        this.loading.set(false);
        if (result.success) {
          this.success.set(result.message);
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

        this.error.set(err.error?.message ?? 'Islem basarisiz.');
      }
    });
  }
}
