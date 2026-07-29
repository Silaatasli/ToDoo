import { Priority } from './team.model';

export enum SprintStatus {
  Planned = 0,
  Active = 1,
  Completed = 2,
  Cancelled = 3
}

export interface SprintTask {
  id: number;
  title: string;
  description?: string | null;
  priority: Priority;
  isCompleted: boolean;
  assignedToUserId?: number | null;
  assignedToEmail?: string | null;
  sprintOrder: number;
  boardColumnId: number;
  boardColumnTitle?: string | null;
  subtaskDoneCount: number;
  subtaskTotal: number;
}

export interface SprintDetail {
  id: number;
  teamId: number;
  boardId: number;
  name: string;
  goal?: string | null;
  status: SprintStatus;
  plannedStartDate: string;
  plannedEndDate: string;
  actualStartDate?: string | null;
  actualEndDate?: string | null;
  displayOrder: number;
  taskCount: number;
  completedTaskCount: number;
  tasks: SprintTask[];
}

export interface BoardKapsam {
  teamId: number;
  teamName: string;
  boardId: number;
  boardName: string;
  backlogTasks: SprintTask[];
  sprints: SprintDetail[];
}

export interface CreateSprintRequest {
  name: string;
  goal?: string | null;
  plannedStartDate: string;
  plannedEndDate: string;
}

export interface UpdateSprintRequest {
  name: string;
  goal?: string | null;
  plannedStartDate: string;
  plannedEndDate: string;
}

export interface SprintAuditEntry {
  id: string;
  teamId: number;
  boardId: number;
  sprintId: number;
  sprintName: string;
  taskId?: number | null;
  userId: number;
  userEmail?: string | null;
  actionType: string;
  oldValue?: string | null;
  newValue?: string | null;
  createdDate: string;
  source: string;
}

export interface CompleteSprintRequest {
  incompleteDestination: 'backlog' | 'sprint';
  targetSprintId?: number | null;
}

export interface CancelSprintRequest {
  taskDestination: 'backlog' | 'sprint';
  targetSprintId?: number | null;
}
