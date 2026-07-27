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

export interface SlaTaskItem {
  id: number;
  title: string;
  dueDate?: string | null;
  completedAt?: string | null;
  priority: number;
}

export interface SlaPerformance {
  teamId: number;
  userId: number;
  displayName?: string | null;
  compliancePercent?: number | null;
  metCount: number;
  breachedCount: number;
  onTrackCount: number;
  recentMet: SlaTaskItem[];
  recentBreached: SlaTaskItem[];
}

export interface TeamSlaMembers {
  teamId: number;
  members: SlaPerformance[];
}
