import { Priority, TeamMember } from '../../../models/team.model';

export interface RemoteTaskDrag {
  userId: number;
  taskId: number;
  sourceColumnId: number;
  hoverColumnId: number;
}

export const PRIORITY_OPTIONS: { value: Priority; label: string }[] = [
  { value: Priority.Low, label: 'Düşük' },
  { value: Priority.Medium, label: 'Orta' },
  { value: Priority.High, label: 'Yüksek' },
  { value: Priority.Critical, label: 'Kritik' }
];

export function priorityLabel(priority: Priority): string {
  return PRIORITY_OPTIONS.find((p) => p.value === priority)?.label ?? '';
}

export function priorityClass(priority: Priority): string {
  switch (priority) {
    case Priority.Critical:
      return 'critical';
    case Priority.High:
      return 'high';
    case Priority.Medium:
      return 'medium';
    default:
      return 'low';
  }
}

export function initial(value: string): string {
  return value.trim().charAt(0).toUpperCase() || '?';
}

export function memberName(member: TeamMember): string {
  const full = [member.firstName, member.lastName].filter((part) => !!part && part.trim()).join(' ').trim();
  return full || member.email;
}
