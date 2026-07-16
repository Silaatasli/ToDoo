import { Component, inject, input, output } from '@angular/core';
import { FormArray, FormBuilder, FormControl, ReactiveFormsModule, Validators } from '@angular/forms';

export interface CreateBoardPayload {
  name: string;
  columnTitles?: string[];
}

const DEFAULT_BOARD_COLUMNS = ['All Tasks', 'In Progress', 'Completed'];

@Component({
  selector: 'app-create-board-modal',
  imports: [ReactiveFormsModule],
  templateUrl: './create-board-modal.html',
  styleUrl: './create-board-modal.scss'
})
export class CreateBoardModalComponent {
  private readonly fb = inject(FormBuilder);

  readonly saving = input(false);
  readonly error = input<string | null>(null);

  readonly close = output<void>();
  readonly submitBoard = output<CreateBoardPayload>();

  readonly boardForm = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(200)]],
    columns: this.fb.array(DEFAULT_BOARD_COLUMNS.map((title) => this.buildColumnControl(title)))
  });

  get boardColumns(): FormArray<FormControl<string>> {
    return this.boardForm.controls.columns;
  }

  private buildColumnControl(value = ''): FormControl<string> {
    return this.fb.nonNullable.control(value, [Validators.maxLength(100)]);
  }

  addColumnField(): void {
    this.boardColumns.push(this.buildColumnControl());
  }

  removeColumnField(index: number): void {
    if (this.boardColumns.length <= 1) {
      return;
    }
    this.boardColumns.removeAt(index);
  }

  onSubmit(): void {
    if (this.boardForm.invalid) {
      this.boardForm.markAllAsTouched();
      return;
    }

    const { name, columns } = this.boardForm.getRawValue();
    const columnTitles = columns.map((title) => title.trim()).filter((title) => title.length > 0);

    this.submitBoard.emit({
      name: name.trim(),
      columnTitles: columnTitles.length > 0 ? columnTitles : undefined
    });
  }

  onClose(): void {
    if (this.saving()) {
      return;
    }
    this.close.emit();
  }
}
