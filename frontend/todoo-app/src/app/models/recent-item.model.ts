export type RecentItemKind = 'task' | 'board' | 'team';

export interface RecentItem {
  key: string;
  kind: RecentItemKind;
  id: number;
  title: string;
  subtitle: string;
  visitedAt: number;
  teamId: number;
  boardId?: number;
}
