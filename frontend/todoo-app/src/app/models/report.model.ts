export interface ReportTaskItem {
  id: number;
  title: string;
  dueDate?: string | null;
  boardColumnTitle?: string | null;
}

export interface TaskReport {
  completedTaskCount: number;
  activeTaskCount: number;
  overdueTaskCount: number;
  upcomingTaskCount: number;
  lowPriorityCount: number;
  mediumPriorityCount: number;
  highPriorityCount: number;
  criticalPriorityCount: number;
  mostUsedCategoryName?: string | null;
  overdueTasks: ReportTaskItem[];
  upcomingTasks: ReportTaskItem[];
}

export interface SlaActiveSprintContext {
  sprintId: number;
  sprintName: string;
  boardId: number;
  boardName: string;
  plannedEndDate: string;
}

export interface SlaTaskItem {
  id: number;
  title: string;
  dueDate?: string | null;
  completedAt?: string | null;
  priority: number;
  sprintId?: number | null;
  sprintName?: string | null;
}

export interface SlaPerformance {
  teamId: number;
  userId: number;
  displayName?: string | null;
  compliancePercent?: number | null;
  metCount: number;
  breachedCount: number;
  onTrackCount: number;
  activeSprints: SlaActiveSprintContext[];
  hasActiveSprint: boolean;
  recentMet: SlaTaskItem[];
  recentBreached: SlaTaskItem[];
}

export interface TeamSlaMembers {
  teamId: number;
  activeSprints: SlaActiveSprintContext[];
  hasActiveSprint: boolean;
  members: SlaPerformance[];
}
