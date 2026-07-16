import { DatePipe } from '@angular/common';
import { Component, input, output } from '@angular/core';
import { TaskListItem } from '../../../../../models/team.model';
import { initial, priorityClass, priorityLabel } from '../../board-ui.utils';

@Component({
  selector: 'app-task-card',
  imports: [DatePipe],
  templateUrl: './task-card.html',
  styleUrl: './task-card.scss'
})
export class TaskCardComponent {
  readonly task = input.required<TaskListItem>();
  readonly dragging = input(false);
  readonly remoteDragging = input(false);
  readonly remoteDragLabel = input<string | null>(null);
  readonly photoUrl = input<string | null>(null);
  readonly pendingAssignment = input(false);

  readonly open = output<void>();
  readonly delete = output<void>();
  readonly dragStart = output<DragEvent>();
  readonly dragEnd = output<void>();

  readonly priorityLabel = priorityLabel;
  readonly priorityClass = priorityClass;
  readonly initial = initial;

  onDragStart(event: DragEvent): void {
    if (event.dataTransfer) {
      event.dataTransfer.effectAllowed = 'move';
      event.dataTransfer.setData('text/plain', String(this.task().id));

      const card = event.currentTarget as HTMLElement | null;
      if (card) {
        const ghost = card.cloneNode(true) as HTMLElement;
        ghost.style.width = `${card.offsetWidth}px`;
        ghost.style.position = 'absolute';
        ghost.style.top = '-9999px';
        ghost.style.left = '-9999px';
        ghost.style.opacity = '0.95';
        document.body.appendChild(ghost);
        event.dataTransfer.setDragImage(ghost, card.offsetWidth / 2, 24);
        requestAnimationFrame(() => ghost.remove());
      }
    }

    this.dragStart.emit(event);
  }

  onDeleteClick(event: MouseEvent): void {
    event.stopPropagation();
    this.delete.emit();
  }
}
