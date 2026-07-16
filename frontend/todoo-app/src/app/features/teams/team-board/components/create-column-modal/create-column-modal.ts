import { Component, inject, input, output } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';

export interface CreateColumnPayload {
  title: string;
}

@Component({
  selector: 'app-create-column-modal',
  imports: [ReactiveFormsModule],
  templateUrl: './create-column-modal.html',
  styleUrl: './create-column-modal.scss'
})
export class CreateColumnModalComponent {
  private readonly fb = inject(FormBuilder);

  readonly saving = input(false);
  readonly error = input<string | null>(null);

  readonly close = output<void>();
  readonly submitColumn = output<CreateColumnPayload>();

  readonly columnForm = this.fb.nonNullable.group({
    title: ['', [Validators.required, Validators.maxLength(100)]]
  });

  onSubmit(): void {
    if (this.columnForm.invalid) {
      this.columnForm.markAllAsTouched();
      return;
    }

    const { title } = this.columnForm.getRawValue();
    this.submitColumn.emit({ title: title.trim() });
  }

  onClose(): void {
    if (this.saving()) {
      return;
    }
    this.close.emit();
  }
}
