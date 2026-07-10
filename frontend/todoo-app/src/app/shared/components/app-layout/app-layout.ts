import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { Router, RouterLink, RouterLinkActive } from '@angular/router';
import { AuthService } from '../../../core/services/auth.service';
import { TeamService } from '../../../core/services/team.service';
import { UserService } from '../../../core/services/user.service';
import { TeamListItem } from '../../../models/team.model';
import { UserProfile } from '../../../models/user.model';

const SIDEBAR_KEY = 'todoo_sidebar_collapsed';

@Component({
  selector: 'app-layout',
  imports: [RouterLink, RouterLinkActive],
  templateUrl: './app-layout.html',
  styleUrl: './app-layout.scss'
})
export class AppLayout implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly teamService = inject(TeamService);
  private readonly userService = inject(UserService);
  private readonly router = inject(Router);

  readonly user = this.auth.getUser();
  readonly teams = signal<TeamListItem[]>([]);
  readonly collapsed = signal<boolean>(localStorage.getItem(SIDEBAR_KEY) === '1');
  readonly profile = signal<UserProfile | null>(null);

  readonly displayName = computed(() => {
    const p = this.profile();
    const full = [p?.firstName, p?.lastName].filter((part) => !!part && part.trim()).join(' ').trim();
    return full || this.user?.email || '';
  });

  ngOnInit(): void {
    this.teamService.getTeams().subscribe({
      next: (teams) => this.teams.set(teams),
      error: () => this.teams.set([])
    });

    this.userService.getMyProfile().subscribe({
      next: (profile) => this.profile.set(profile),
      error: () => this.profile.set(null)
    });
  }

  toggleSidebar(): void {
    const next = !this.collapsed();
    this.collapsed.set(next);
    localStorage.setItem(SIDEBAR_KEY, next ? '1' : '0');
  }

  logout(): void {
    this.auth.logout();
    void this.router.navigate(['/login']);
  }

  teamInitial(name: string): string {
    return name.trim().charAt(0).toUpperCase() || '?';
  }
}
