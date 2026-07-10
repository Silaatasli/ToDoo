import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { ReportService } from '../../core/services/report.service';
import { TeamService } from '../../core/services/team.service';
import { ReportTaskItem, TaskReport } from '../../models/report.model';
import { TeamDetail } from '../../models/team.model';
import { AppLayout } from '../../shared/components/app-layout/app-layout';

@Component({
  selector: 'app-team-reports',
  imports: [AppLayout, RouterLink, DatePipe],
  templateUrl: './team-reports.html',
  styleUrl: './team-reports.scss'
})
export class TeamReports implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly teamService = inject(TeamService);
  private readonly reportService = inject(ReportService);
  private readonly destroyRef = inject(DestroyRef);

  readonly teamId = signal<number | null>(null);
  readonly team = signal<TeamDetail | null>(null);
  readonly report = signal<TaskReport | null>(null);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly showTasksModal = signal(false);
  readonly tasksModalTitle = signal('');
  readonly tasksModalItems = signal<ReportTaskItem[]>([]);

  readonly totalTasks = computed(() => {
    const data = this.report();
    if (!data) {
      return 0;
    }
    return data.activeTaskCount + data.completedTaskCount;
  });

  readonly completionRate = computed(() => {
    const total = this.totalTasks();
    const data = this.report();
    if (!data || total === 0) {
      return 0;
    }
    return Math.round((data.completedTaskCount / total) * 100);
  });

  readonly priorityTotal = computed(() => {
    const data = this.report();
    if (!data) {
      return 0;
    }
    return data.lowPriorityCount + data.mediumPriorityCount + data.highPriorityCount + data.criticalPriorityCount;
  });

  readonly priorityGradient = computed(() => {
    const data = this.report();
    const total = this.priorityTotal();
    if (!data || total === 0) {
      return 'conic-gradient(#e6eaef 0deg 360deg)';
    }

    const low = (data.lowPriorityCount / total) * 360;
    const medium = (data.mediumPriorityCount / total) * 360;
    const high = (data.highPriorityCount / total) * 360;
    const critical = (data.criticalPriorityCount / total) * 360;

    const lowEnd = low;
    const mediumEnd = lowEnd + medium;
    const highEnd = mediumEnd + high;
    const criticalEnd = highEnd + critical;

    return `conic-gradient(
      #2f9e44 0deg ${lowEnd}deg,
      #1971c2 ${lowEnd}deg ${mediumEnd}deg,
      #f08c00 ${mediumEnd}deg ${highEnd}deg,
      #e03131 ${highEnd}deg ${criticalEnd}deg,
      #e6eaef ${criticalEnd}deg 360deg
    )`;
  });

  ngOnInit(): void {
    this.route.paramMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((params) => {
      const id = Number(params.get('id'));
      if (Number.isNaN(id)) {
        this.teamId.set(null);
        this.error.set('Geçersiz takım.');
        this.loading.set(false);
        return;
      }

      this.teamId.set(id);
      this.load();
    });
  }

  load(): void {
    const id = this.teamId();
    if (id === null) {
      return;
    }

    this.loading.set(true);
    this.error.set(null);

    this.teamService.getTeam(id).subscribe({
      next: (team) => this.team.set(team),
      error: () => this.team.set(null)
    });

    this.reportService.getTaskSummary(id).subscribe({
      next: (report) => {
        this.report.set(report);
        this.loading.set(false);
      },
      error: (err: HttpErrorResponse) => {
        this.loading.set(false);
        this.error.set(
          err.status === 0
            ? "API'ye bağlanılamadı. Backend çalışıyor mu?"
            : (err.error?.message ?? 'Rapor yüklenemedi.')
        );
      }
    });
  }

  openTasksModal(title: string, tasks: ReportTaskItem[]): void {
    this.tasksModalTitle.set(title);
    this.tasksModalItems.set(tasks);
    this.showTasksModal.set(true);
  }

  closeTasksModal(): void {
    this.showTasksModal.set(false);
  }
}
