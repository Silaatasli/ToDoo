import { Component, inject, input, output } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Category } from '../../../../../models/category.model';
import { Priority, TeamMember } from '../../../../../models/team.model';
import { PRIORITY_OPTIONS } from '../../board-ui.utils';

export interface CreateTaskPayload {
  title: string;
  description: string | null;
  categoryId: number | null;
  priority: Priority;
  dueDate: string | null;
  assignedToUserId: number | null;
}

@Component({
  selector: 'app-create-task-modal',
  imports: [ReactiveFormsModule],
  templateUrl: './create-task-modal.html',
  styleUrl: './create-task-modal.scss'
})
export class CreateTaskModalComponent {
  private readonly fb = inject(FormBuilder);

  readonly saving = input(false);
  readonly error = input<string | null>(null);
  readonly categories = input<Category[]>([]);
  readonly members = input<TeamMember[]>([]);

  readonly close = output<void>();
  readonly submitTask = output<CreateTaskPayload>();

  readonly priorityOptions = PRIORITY_OPTIONS;

  readonly taskForm = this.fb.nonNullable.group({
    title: ['', [Validators.required, Validators.maxLength(200)]],
    description: [''],
    priority: [Priority.Medium, [Validators.required]],
    categoryId: [''],
    dueDate: [''],
    assignedToUserId: ['']
  });

  onSubmit(): void {
    if (this.taskForm.invalid) {
      this.taskForm.markAllAsTouched();
      return;
    }

    const raw = this.taskForm.getRawValue();
    this.submitTask.emit({
      title: raw.title.trim(),
      description: raw.description.trim() || null,
      categoryId: raw.categoryId ? Number(raw.categoryId) : null,
      priority: Number(raw.priority) as Priority,
      dueDate: raw.dueDate || null,
      assignedToUserId: raw.assignedToUserId ? Number(raw.assignedToUserId) : null
    });
  }

  onClose(): void {
    if (this.saving()) {
      return;
    }
    this.close.emit();
  }
}
