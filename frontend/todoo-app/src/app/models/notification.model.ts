export type NotificationType =
  | 'TaskAssigned'
  | 'CommentReply'
  | 'TeamMemberAdded'
  | 'Announcement'
  | 'Mention'
  | 'SprintStarted';

export interface AppNotification {
  id: string;
  type: NotificationType | string;
  title: string;
  body: string;
  teamId?: number | null;
  boardId?: number | null;
  taskId?: number | null;
  announcementId?: number | null;
  sprintId?: number | null;
  isRead: boolean;
  createdAtUtc: string;
}

export interface NotificationListResponse {
  items: AppNotification[];
  unreadCount: number;
}

export interface NotificationReceivedPayload {
  notification: AppNotification;
  unreadCount: number;
}
