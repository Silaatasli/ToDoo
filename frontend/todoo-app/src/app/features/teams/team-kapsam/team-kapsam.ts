import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';
import { TeamService } from '../../../core/services/team.service';
import { TeamDetail } from '../../../models/team.model';
import { AppLayout } from '../../../shared/components/app-layout/app-layout';
import { TeamWorkspaceShell } from '../../../shared/components/team-workspace-shell/team-workspace-shell';

@Component({
  selector: 'app-team-kapsam',
  imports: [AppLayout, TeamWorkspaceShell],
  templateUrl: './team-kapsam.html',
  styleUrl: './team-kapsam.scss'
})
export class TeamKapsamPage implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly teamService = inject(TeamService);
  private readonly destroyRef = inject(DestroyRef);

  readonly teamId = signal<number | null>(null);
  readonly team = signal<TeamDetail | null>(null);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

  ngOnInit(): void {
    this.route.paramMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((params) => {
      const id = Number(params.get('id'));
      if (!Number.isFinite(id) || id <= 0) {
        this.error.set('Geçersiz takım.');
        this.loading.set(false);
        return;
      }

      this.teamId.set(id);
      this.loadTeam(id);
    });
  }

  private loadTeam(teamId: number): void {
    this.loading.set(true);
    this.error.set(null);
    this.teamService.getTeam(teamId).subscribe({
      next: (team) => {
        this.team.set(team);
        this.loading.set(false);
      },
      error: (err: HttpErrorResponse) => {
        this.error.set(err.error?.message ?? 'Takım yüklenemedi.');
        this.loading.set(false);
      }
    });
  }
}
