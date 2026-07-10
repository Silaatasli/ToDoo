import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, OnInit, signal } from '@angular/core';
import { FormArray, FormBuilder, FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../../../core/services/auth.service';
import { TeamService } from '../../../core/services/team.service';
import { AppLayout } from '../../../shared/components/app-layout/app-layout';
import { TeamListItem } from '../../../models/team.model';

@Component({
  selector: 'app-team-list',
  imports: [AppLayout, ReactiveFormsModule, RouterLink],
  templateUrl: './team-list.html',
  styleUrl: './team-list.scss'
})
export class TeamList implements OnInit {
  private readonly teamService = inject(TeamService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly fb = inject(FormBuilder);

  readonly teams = signal<TeamListItem[]>([]);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly showCreateModal = signal(false);
  readonly creating = signal(false);
  readonly createError = signal<string | null>(null);

  readonly user = this.auth.getUser();

  private readonly defaultColumns = ['All Tasks', 'In Progress', 'Completed'];

  readonly createForm = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(200)]],
    columns: this.fb.array(this.defaultColumns.map((title) => this.buildColumnControl(title)))
  });

  get columns(): FormArray<FormControl<string>> {
    return this.createForm.controls.columns;
  }

  private buildColumnControl(value = ''): FormControl<string> {
    return this.fb.nonNullable.control(value, [Validators.maxLength(100)]);
  }

  addColumnField(): void {
    this.columns.push(this.buildColumnControl());
  }

  removeColumnField(index: number): void {
    this.columns.removeAt(index);
  }

  private resetColumns(): void {
    this.columns.clear();
    this.defaultColumns.forEach((title) => this.columns.push(this.buildColumnControl(title)));
  }

  ngOnInit(): void {
    this.loadTeams();
  }

  loadTeams(): void {
    this.loading.set(true);
    this.error.set(null);

    this.teamService.getTeams().subscribe({
      next: (teams) => {
        this.teams.set(teams);
        this.loading.set(false);
      },
      error: (err: HttpErrorResponse) => {
        this.loading.set(false);
        this.error.set(
          err.status === 0
            ? "API'ye bağlanılamadı. Backend çalışıyor mu?"
            : (err.error?.message ?? 'Takımlar yüklenemedi.')
        );
      }
    });
  }

  openCreateModal(): void {
    this.createForm.reset({ name: '' });
    this.resetColumns();
    this.createError.set(null);
    this.showCreateModal.set(true);
  }

  closeCreateModal(): void {
    if (this.creating()) {
      return;
    }

    this.showCreateModal.set(false);
    this.createError.set(null);
  }

  submitCreate(): void {
    if (this.createForm.invalid) {
      this.createForm.markAllAsTouched();
      return;
    }

    this.creating.set(true);
    this.createError.set(null);

    const { name, columns } = this.createForm.getRawValue();
    const columnTitles = columns.map((title) => title.trim()).filter((title) => title.length > 0);

    this.teamService.createTeam({
      name: name.trim(),
      columnTitles: columnTitles.length > 0 ? columnTitles : undefined
    }).subscribe({
      next: (team) => {
        this.creating.set(false);
        this.showCreateModal.set(false);
        void this.router.navigate(['/teams', team.id, 'board']);
      },
      error: (err: HttpErrorResponse) => {
        this.creating.set(false);
        this.createError.set(
          err.status === 0
            ? "API'ye bağlanılamadı. Backend çalışıyor mu?"
            : (err.error?.message ?? 'Takım oluşturulamadı.')
        );
      }
    });
  }

  isLeader(team: TeamListItem): boolean {
    return team.leaderUserId === this.user?.userId;
  }

  teamInitial(name: string): string {
    return name.trim().charAt(0).toUpperCase() || '?';
  }
}
