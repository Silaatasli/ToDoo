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
