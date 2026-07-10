export interface TeamListItem {
  id: number;
  name: string;
  leaderUserId: number;
  leaderEmail: string;
  memberCount: number;
  createdDate: string;
}

export interface TeamMember {
  userId: number;
  email: string;
  firstName?: string | null;
  lastName?: string | null;
  isLeader: boolean;
  joinedDate: string;
}

export interface BoardColumn {
  id: number;
  title: string;
  displayOrder: number;
  isCompletedColumn: boolean;
}

export interface TeamDetail {
  id: number;
  name: string;
  leaderUserId: number;
  leaderEmail: string;
  createdDate: string;
  members: TeamMember[];
  boardColumns: BoardColumn[];
}

export interface BoardColumnWithTasks extends BoardColumn {
  tasks: TaskListItem[];
}

export interface TeamBoard {
  teamId: number;
  teamName: string;
  columns: BoardColumnWithTasks[];
}

export interface CreateTeamRequest {
  name: string;
  columnTitles?: string[];
}

export interface AddColumnRequest {
  title: string;
}

export interface ReorderColumnsRequest {
  columnIds: number[];
}

export interface AddMemberRequest {
  email: string;
}

export interface CreateTeamTaskRequest {
  title: string;
  description?: string | null;
  categoryId?: number | null;
  priority: Priority;
  startDate: string;
  dueDate?: string | null;
  boardColumnId?: number | null;
  assignedToUserId?: number | null;
}

export enum Priority {
  Low = 1,
  Medium = 2,
  High = 3,
  Critical = 4
}

export enum AssignmentStatus {
  None = 0,
  Pending = 1,
  Accepted = 2,
  Declined = 3
}

export interface TaskDetail {
  id: number;
  teamId: number;
  teamName?: string | null;
  isPersonalTeam: boolean;
  boardColumnId: number;
  boardColumnTitle: string;
  title: string;
  description?: string | null;
  categoryId?: number | null;
  categoryName?: string | null;
  priority: Priority;
  createdDate: string;
  startDate: string;
  dueDate?: string | null;
  isCompleted: boolean;
  createdByUserId: number;
  createdByEmail: string;
  assignedToUserId?: number | null;
  assignedToEmail?: string | null;
  assignmentStatus: AssignmentStatus;
}

export interface UpdateTaskRequest {
  title: string;
  description?: string | null;
  categoryId?: number | null;
  priority: Priority;
  startDate: string;
  dueDate?: string | null;
}

export interface TaskListItem {
  id: number;
  title: string;
  description?: string | null;
  categoryId?: number | null;
  categoryName?: string | null;
  priority: Priority;
  startDate: string;
  dueDate?: string | null;
  isCompleted: boolean;
  teamId: number;
  teamName?: string | null;
  isPersonalTeam: boolean;
  boardColumnId: number;
  boardColumnTitle?: string | null;
  assignedToUserId?: number | null;
  assignedToEmail?: string | null;
  assignmentStatus: AssignmentStatus;
}

export enum TaskActivityAction {
  TaskCreated = 1,
  Assigned = 2,
  ColumnChanged = 3,
  Updated = 4,
  Deleted = 5,
  AssignmentAccepted = 6,
  AssignmentDeclined = 7,
  AttachmentAdded = 8,
  AttachmentDeleted = 9,
  CommentAdded = 10,
  CommentDeleted = 11
}

export interface TaskAttachment {
  id: number;
  taskId: number;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  uploadedByUserId: number;
  uploadedByEmail: string;
  createdDate: string;
}

export interface TeamActivityLog {
  id: number;
  taskId: number;
  userId: number;
  userEmail: string;
  actionType: TaskActivityAction;
  oldValue?: string | null;
  newValue?: string | null;
  createdDate: string;
}

export interface CommentAttachment {
  id: number;
  commentId: number;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  uploadedByUserId: number;
  uploadedByEmail: string;
  createdDate: string;
}

export interface TaskComment {
  id: number;
  taskId: number;
  parentCommentId?: number | null;
  body: string;
  authorUserId: number;
  authorEmail: string;
  createdDate: string;
  attachments: CommentAttachment[];
  replies: TaskComment[];
}

export interface CreateCommentRequest {
  body: string;
  parentCommentId?: number | null;
}
