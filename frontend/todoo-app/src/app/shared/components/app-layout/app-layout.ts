import { Component, computed, DestroyRef, HostListener, inject, OnDestroy, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { Router, RouterLink, RouterLinkActive } from '@angular/router';
import { catchError, debounceTime, distinctUntilChanged, of, switchMap } from 'rxjs';
import { AuthService } from '../../../core/services/auth.service';
import { NotificationHubService } from '../../../core/services/notification-hub.service';
import { NotificationService } from '../../../core/services/notification.service';
import { ProfilePhotoCacheService } from '../../../core/services/profile-photo-cache.service';
import { RecentItemsService } from '../../../core/services/recent-items.service';
import { SearchService } from '../../../core/services/search.service';
import { ThemeService } from '../../../core/services/theme.service';
import { TeamService } from '../../../core/services/team.service';
import { UserService } from '../../../core/services/user.service';
import {
  GlobalSearchBoard,
  GlobalSearchPerson,
  GlobalSearchResult,
  GlobalSearchTask,
  GlobalSearchTeam
} from '../../../models/search.model';
import { AppNotification } from '../../../models/notification.model';
import { RecentItem } from '../../../models/recent-item.model';
import { TeamListItem } from '../../../models/team.model';
import { UserProfile } from '../../../models/user.model';

const SIDEBAR_KEY = 'todoo_sidebar_collapsed';

type SearchNavItem =
  | { kind: 'task'; item: GlobalSearchTask }
  | { kind: 'board'; item: GlobalSearchBoard }
  | { kind: 'team'; item: GlobalSearchTeam }
  | { kind: 'person'; item: GlobalSearchPerson };

@Component({
  selector: 'app-layout',
  imports: [RouterLink, RouterLinkActive, ReactiveFormsModule],
  templateUrl: './app-layout.html',
  styleUrl: './app-layout.scss'
})
export class AppLayout implements OnInit, OnDestroy {
  private readonly auth = inject(AuthService);
  private readonly teamService = inject(TeamService);
  private readonly userService = inject(UserService);
  private readonly searchService = inject(SearchService);
  private readonly recentStore = inject(RecentItemsService);
  private readonly themeService = inject(ThemeService);
  private readonly photoCache = inject(ProfilePhotoCacheService);
  private readonly notificationService = inject(NotificationService);
  private readonly notificationHub = inject(NotificationHubService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  readonly user = this.auth.getUser();
  readonly teams = signal<TeamListItem[]>([]);
  readonly collapsed = signal<boolean>(localStorage.getItem(SIDEBAR_KEY) === '1');
  readonly profile = signal<UserProfile | null>(null);
  readonly theme = this.themeService.theme;

  readonly notifications = this.notificationService.items;
  readonly unreadCount = this.notificationService.unreadCount;
  readonly toasts = this.notificationService.toasts;
  readonly notifOpen = signal(false);
  readonly notifUnreadOnly = signal(false);
  readonly notifMenuOpen = signal(false);

  readonly visibleNotifications = computed(() => {
    const items = this.notifications();
    return this.notifUnreadOnly() ? items.filter((item) => !item.isRead) : items;
  });

  readonly searchControl = new FormControl('', { nonNullable: true });
  readonly searchOpen = signal(false);
  readonly searchLoading = signal(false);
  readonly searchResults = signal<GlobalSearchResult | null>(null);
  readonly searchQuery = signal('');
  readonly searchActiveIndex = signal(-1);

  readonly recentOpen = signal(false);
  readonly recentItems = this.recentStore.items;
  private recentCloseTimer: ReturnType<typeof setTimeout> | null = null;

  private readonly onBrowserNotificationClick = (event: Event): void => {
    const custom = event as CustomEvent<AppNotification>;
    if (custom.detail) {
      this.openNotification(custom.detail, new Event('click'));
    }
  };

  readonly displayName = computed(() => {
    const p = this.profile();
    const full = [p?.firstName, p?.lastName].filter((part) => !!part && part.trim()).join(' ').trim();
    return full || 'Kullanıcı';
  });

  readonly photoUrl = computed(() => {
    const p = this.profile();
    if (!p?.hasProfilePhoto) {
      return null;
    }
    return this.photoCache.photoUrl(p.id);
  });

  readonly hasSearchResults = computed(() => {
    const results = this.searchResults();
    if (!results) {
      return false;
    }
    return results.teams.length > 0
      || results.boards.length > 0
      || results.tasks.length > 0
      || results.people.length > 0;
  });

  readonly flatSearchItems = computed((): SearchNavItem[] => {
    const results = this.searchResults();
    if (!results) {
      return [];
    }
    return [
      ...results.tasks.map((item): SearchNavItem => ({ kind: 'task', item })),
      ...results.boards.map((item): SearchNavItem => ({ kind: 'board', item })),
      ...results.teams.map((item): SearchNavItem => ({ kind: 'team', item })),
      ...results.people.map((item): SearchNavItem => ({ kind: 'person', item }))
    ];
  });

  ngOnInit(): void {
    this.recentStore.load();
    this.notificationService.load();
    void this.notificationHub.connect();
    window.addEventListener('todoo-notification-click', this.onBrowserNotificationClick);

    this.teamService.getTeams().subscribe({
      next: (teams) => this.teams.set(teams),
      error: () => this.teams.set([])
    });

    this.userService.getMyProfile().subscribe({
      next: (profile) => {
        this.profile.set(profile);
        this.photoCache.ensure(profile.id, profile.hasProfilePhoto);
      },
      error: () => this.profile.set(null)
    });

    this.searchControl.valueChanges
      .pipe(
        debounceTime(250),
        distinctUntilChanged(),
        switchMap((raw) => {
          const query = raw.trim();
          this.searchQuery.set(query);
          this.searchActiveIndex.set(-1);
          if (query.length < 3) {
            this.searchLoading.set(false);
            this.searchResults.set(null);
            return of(null);
          }

          this.searchLoading.set(true);
          return this.searchService.search(query).pipe(
            catchError(() => of<GlobalSearchResult>({ teams: [], boards: [], tasks: [], people: [] }))
          );
        }),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe((results) => {
        this.searchLoading.set(false);
        this.searchResults.set(results);
        this.searchActiveIndex.set(results && this.hasAnyResult(results) ? 0 : -1);
        if (results) {
          this.photoCache.ensureMany(
            results.people.map((person) => ({
              userId: person.id,
              hasProfilePhoto: person.hasProfilePhoto
            }))
          );
        }
      });
  }

  ngOnDestroy(): void {
    window.removeEventListener('todoo-notification-click', this.onBrowserNotificationClick);
    void this.notificationHub.disconnect();
  }

  @HostListener('document:click')
  onDocumentClick(): void {
    this.searchOpen.set(false);
    this.recentOpen.set(false);
    this.notifOpen.set(false);
    this.notifMenuOpen.set(false);
  }

  openSearch(event: Event): void {
    event.stopPropagation();
    this.searchOpen.set(true);
  }

  onSearchKeydown(event: KeyboardEvent): void {
    const items = this.flatSearchItems();
    const open = this.searchOpen() && this.searchQuery().length > 0;

    if (event.key === 'Escape') {
      event.preventDefault();
      this.searchOpen.set(false);
      this.searchActiveIndex.set(-1);
      return;
    }

    if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
      if (!open || items.length === 0) {
        return;
      }
      event.preventDefault();
      this.searchOpen.set(true);
      const current = this.searchActiveIndex();
      const next =
        event.key === 'ArrowDown'
          ? (current + 1) % items.length
          : (current <= 0 ? items.length - 1 : current - 1);
      this.searchActiveIndex.set(next);
      this.scrollActiveIntoView();
      return;
    }

    if (event.key === 'Enter') {
      if (!open || items.length === 0) {
        return;
      }
      const index = this.searchActiveIndex();
      const selected = items[index] ?? items[0];
      if (!selected) {
        return;
      }
      event.preventDefault();
      this.activateNavItem(selected);
    }
  }

  setSearchActiveIndex(index: number): void {
    this.searchActiveIndex.set(index);
  }

  toggleSidebar(): void {
    const next = !this.collapsed();
    this.collapsed.set(next);
    localStorage.setItem(SIDEBAR_KEY, next ? '1' : '0');
  }

  toggleTheme(): void {
    this.themeService.toggle();
  }

  toggleNotifications(event: Event): void {
    event.stopPropagation();
    this.notifOpen.update((open) => !open);
    this.notifMenuOpen.set(false);
    this.searchOpen.set(false);
    this.recentOpen.set(false);
  }

  toggleNotifUnreadOnly(event: Event): void {
    event.stopPropagation();
    this.notifUnreadOnly.update((value) => !value);
  }

  toggleNotifMenu(event: Event): void {
    event.stopPropagation();
    this.notifMenuOpen.update((open) => !open);
  }

  markAllNotificationsRead(event: Event): void {
    event.stopPropagation();
    this.notifMenuOpen.set(false);
    this.notificationService.markAllRead().subscribe();
  }

  markNotificationRead(item: AppNotification, event: Event): void {
    event.stopPropagation();
    if (item.isRead) {
      return;
    }
    this.notificationService.markRead(item.id).subscribe();
  }

  openNotification(item: AppNotification, event: Event): void {
    event.stopPropagation();
    this.notifOpen.set(false);
    this.notifMenuOpen.set(false);
    this.notificationService.dismissToast(item.id);

    if (!item.isRead) {
      this.notificationService.markRead(item.id).subscribe();
    }

    if (item.teamId && item.boardId && item.taskId) {
      void this.router.navigate(['/teams', item.teamId, 'boards', item.boardId], {
        queryParams: { taskId: item.taskId }
      });
      return;
    }

    if (item.teamId && item.boardId) {
      void this.router.navigate(['/teams', item.teamId, 'boards', item.boardId]);
      return;
    }

    if (item.teamId) {
      void this.router.navigate(['/teams', item.teamId, 'board']);
    }
  }

  dismissToast(item: AppNotification, event: Event): void {
    event.stopPropagation();
    this.notificationService.dismissToast(item.id);
  }

  notificationTypeLabel(type: string): string {
    switch (type) {
      case 'TaskAssigned':
        return 'Görev atandı';
      case 'CommentReply':
        return 'Yorum yanıtı';
      case 'TeamMemberAdded':
        return 'Takıma eklendiniz';
      case 'Announcement':
        return 'Takım duyurusu';
      case 'Mention':
        return 'Sizden bahsedildi';
      default:
        return 'Bildirim';
    }
  }

  notificationTypeTone(type: string): string {
    switch (type) {
      case 'TaskAssigned':
        return 'tone-task';
      case 'CommentReply':
        return 'tone-reply';
      case 'TeamMemberAdded':
        return 'tone-team';
      case 'Announcement':
        return 'tone-announce';
      case 'Mention':
        return 'tone-mention';
      default:
        return 'tone-default';
    }
  }

  notificationRelativeTime(value: string): string {
    const date = new Date(value);
    if (Number.isNaN(date.getTime())) {
      return '';
    }

    const diffMs = Date.now() - date.getTime();
    const diffSec = Math.max(0, Math.floor(diffMs / 1000));
    if (diffSec < 60) {
      return 'Az önce';
    }

    const diffMin = Math.floor(diffSec / 60);
    if (diffMin < 60) {
      return `${diffMin} dk önce`;
    }

    const diffHour = Math.floor(diffMin / 60);
    if (diffHour < 24) {
      return `${diffHour} sa önce`;
    }

    const diffDay = Math.floor(diffHour / 24);
    if (diffDay < 7) {
      return `${diffDay} g önce`;
    }

    return date.toLocaleDateString('tr-TR', { day: 'numeric', month: 'short' });
  }

  onRecentEnter(): void {
    if (this.recentCloseTimer) {
      clearTimeout(this.recentCloseTimer);
      this.recentCloseTimer = null;
    }
    this.recentOpen.set(true);
  }

  onRecentLeave(): void {
    if (this.recentCloseTimer) {
      clearTimeout(this.recentCloseTimer);
    }
    this.recentCloseTimer = setTimeout(() => {
      this.recentOpen.set(false);
      this.recentCloseTimer = null;
    }, 180);
  }

  toggleRecentMenu(event: Event): void {
    event.stopPropagation();
    this.recentOpen.update((open) => !open);
  }

  openRecentItem(item: RecentItem, event: Event): void {
    event.stopPropagation();
    this.recentOpen.set(false);
    this.recentStore.navigate(item);
  }

  recentKindLabel(kind: RecentItem['kind']): string {
    return this.recentStore.kindLabel(kind);
  }

  logout(): void {
    void this.notificationHub.disconnect();
    this.auth.logout();
    void this.router.navigate(['/login']);
  }

  teamInitial(name: string): string {
    return name.trim().charAt(0).toUpperCase() || '?';
  }

  personPhotoUrl(person: GlobalSearchPerson): string | null {
    return this.photoCache.photoUrl(person.id);
  }

  selectTeam(team: GlobalSearchTeam, event: Event): void {
    event.stopPropagation();
    this.activateNavItem({ kind: 'team', item: team });
  }

  selectBoard(board: GlobalSearchBoard, event: Event): void {
    event.stopPropagation();
    this.activateNavItem({ kind: 'board', item: board });
  }

  selectTask(task: GlobalSearchTask, event: Event): void {
    event.stopPropagation();
    this.activateNavItem({ kind: 'task', item: task });
  }

  selectPerson(person: GlobalSearchPerson, event: Event): void {
    event.stopPropagation();
    this.activateNavItem({ kind: 'person', item: person });
  }

  private activateNavItem(entry: SearchNavItem): void {
    this.closeSearch();
    if (entry.kind === 'task') {
      this.recentStore.recordTask({
        taskId: entry.item.id,
        title: entry.item.title,
        teamId: entry.item.teamId,
        teamName: entry.item.teamName,
        boardId: entry.item.boardId,
        boardName: 'Pano',
        boardColumnTitle: entry.item.boardColumnTitle
      });
      const boardPath = entry.item.boardId
        ? ['/teams', entry.item.teamId, 'boards', entry.item.boardId]
        : ['/teams', entry.item.teamId, 'board'];
      void this.router.navigate(boardPath, {
        queryParams: { taskId: entry.item.id }
      });
      return;
    }
    if (entry.kind === 'board') {
      this.recentStore.recordBoard({
        boardId: entry.item.id,
        boardName: entry.item.name,
        teamId: entry.item.teamId,
        teamName: entry.item.teamName
      });
      void this.router.navigate(['/teams', entry.item.teamId, 'boards', entry.item.id]);
      return;
    }
    if (entry.kind === 'team') {
      this.recentStore.recordTeam({
        teamId: entry.item.id,
        teamName: entry.item.name
      });
      void this.router.navigate(['/teams', entry.item.id, 'board']);
      return;
    }
    void this.router.navigate(['/profile', entry.item.id]);
  }

  private closeSearch(): void {
    this.searchOpen.set(false);
    this.searchControl.setValue('', { emitEvent: false });
    this.searchQuery.set('');
    this.searchResults.set(null);
    this.searchLoading.set(false);
    this.searchActiveIndex.set(-1);
  }

  private hasAnyResult(results: GlobalSearchResult): boolean {
    return results.teams.length > 0
      || results.boards.length > 0
      || results.tasks.length > 0
      || results.people.length > 0;
  }

  private scrollActiveIntoView(): void {
    queueMicrotask(() => {
      document
        .querySelector<HTMLElement>('.global-search-item.is-active')
        ?.scrollIntoView({ block: 'nearest' });
    });
  }
}
