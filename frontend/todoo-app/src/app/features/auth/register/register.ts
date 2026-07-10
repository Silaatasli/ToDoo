import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../../../core/services/auth.service';

@Component({
  selector: 'app-register',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './register.html',
  styleUrl: './register.scss'
})
export class Register {
  private readonly fb = inject(FormBuilder);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  readonly error = signal<string | null>(null);
  readonly loading = signal(false);

  readonly form = this.fb.nonNullable.group({
    firstName: ['', [Validators.required, Validators.maxLength(100)]],
    lastName: ['', [Validators.required, Validators.maxLength(100)]],
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required, Validators.minLength(6)]],
    confirmPassword: ['', [Validators.required]]
  });

  submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const { firstName, lastName, email, password, confirmPassword } = this.form.getRawValue();

    if (password !== confirmPassword) {
      this.error.set('Sifreler eslesmiyor.');
      return;
    }

    this.loading.set(true);
    this.error.set(null);

    this.auth.register(firstName.trim(), lastName.trim(), email, password).subscribe({
      next: (result) => {
        this.loading.set(false);

        if (result.success) {
          void this.router.navigate(['/teams']);
          return;
        }

        this.error.set(result.message);
      },
      error: (err: HttpErrorResponse) => {
        this.loading.set(false);

        if (err.status === 0) {
          this.error.set(
            'API\'ye baglanilamadi. Backend calisiyor mu? (dotnet run --launch-profile http)'
          );
          return;
        }

        this.error.set(err.error?.message ?? 'Kayit basarisiz.');
      }
    });
  }
}
