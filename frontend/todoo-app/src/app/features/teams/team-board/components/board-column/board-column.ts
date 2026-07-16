import { Component, computed, input, output } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { AssignmentStatus, BoardColumnWithTasks, TaskListItem } from '../../../../../models/team.model';
import { initial, priorityClass, priorityLabel, RemoteTaskDrag } from '../../board-ui.utils';
import { TaskCardComponent } from '../task-card/task-card';

@Component({
  selector: 'app-board-column',
  imports: [ReactiveFormsModule, TaskCardComponent],
  templateUrl: './board-column.html',
  styleUrl: './board-column.scss'
})
export class BoardColumnComponent {
  readonly column = input.required<BoardColumnWithTasks>();
  readonly isLeader = input(false);
  readonly editingColumnId = input<number | null>(null);
  readonly editColumnForm = input.required<FormGroup<{ title: FormControl<string> }>>();
  readonly savingColumnEdit = input(false);
  readonly editColumnError = input<string | null>(null);
  readonly draggedTaskId = input<number | null>(null);
  readonly dragOverColumnId = input<number | null>(null);
  readonly dragOverReorderColumnId = input<number | null>(null);
  readonly remoteTaskDrags = input<RemoteTaskDrag[]>([]);
  readonly taskByIdFn = input.required<(id: number) => TaskListItem | null>();
  readonly remoteDragLabelFn = input.required<(userId: number) => string>();
  readonly photoUrlFn = input.required<(userId: number | null | undefined) => string | null>();

  readonly dragOver = output<DragEvent>();
  readonly dragLeave = output<void>();
  readonly drop = output<DragEvent>();
  readonly columnDragStart = output<void>();
  readonly columnDragEnd = output<void>();
  readonly startEdit = output<void>();
  readonly saveEdit = output<void>();
  readonly cancelEdit = output<void>();
  readonly addTask = output<void>();
  readonly openTask = output<TaskListItem>();
  readonly deleteTask = output<TaskListItem>();
  readonly taskDragStart = output<{ event: DragEvent; task: TaskListItem }>();
  readonly taskDragEnd = output<void>();

  readonly assignmentStatus = AssignmentStatus;
  readonly priorityLabel = priorityLabel;
  readonly priorityClass = priorityClass;
  readonly initial = initial;

  readonly isEditing = computed(() => this.editingColumnId() === this.column().id);

  remoteDropPreviews(): RemoteTaskDrag[] {
    const columnId = this.column().id;
    return this.remoteTaskDrags().filter(
      (drag) => drag.hoverColumnId === columnId && drag.hoverColumnId !== drag.sourceColumnId
    );
  }

  isRemoteDraggingTask(taskId: number): boolean {
    const columnId = this.column().id;
    return this.remoteTaskDrags().some(
      (drag) => drag.taskId === taskId && drag.sourceColumnId === columnId && drag.hoverColumnId !== columnId
    );
  }

  remoteDragInPlace(taskId: number): RemoteTaskDrag | null {
    return (
      this.remoteTaskDrags().find(
        (drag) => drag.taskId === taskId && drag.hoverColumnId === drag.sourceColumnId
      ) ?? null
    );
  }

  isAssignmentPending(task: TaskListItem): boolean {
    return task.assignmentStatus === AssignmentStatus.Pending;
  }
}
