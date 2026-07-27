import { Component, input, output } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';

export type TeamWorkspaceTab = 'pano' | 'kapsam' | 'duyurular' | 'raporlar';

@Component({
  selector: 'app-team-workspace-shell',
  imports: [RouterLink, RouterLinkActive],
  templateUrl: './team-workspace-shell.html',
  styleUrl: './team-workspace-shell.scss'
})
export class TeamWorkspaceShell {
  readonly teamId = input.required<number | null>();
  readonly teamName = input<string>('Takım');
  readonly activeTab = input<TeamWorkspaceTab>('pano');
  readonly showMembersButton = input(true);

  readonly openMembers = output<void>();

  teamInitial(): string {
    const name = this.teamName().trim();
    if (!name) {
      return '?';
    }
    return name.charAt(0).toUpperCase();
  }
}
