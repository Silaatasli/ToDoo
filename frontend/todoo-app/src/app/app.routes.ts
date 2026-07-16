import { Routes } from '@angular/router';
import { authGuard } from './core/guards/auth.guard';
import { ForgotPassword } from './features/auth/forgot-password/forgot-password';
import { Login } from './features/auth/login/login';
import { Register } from './features/auth/register/register';
import { ResetPassword } from './features/auth/reset-password/reset-password';
import { Profile } from './features/profile/profile';
import { TeamReports } from './features/reports/team-reports';
import { TeamBoard } from './features/teams/team-board/team-board';
import { TeamList } from './features/teams/team-list/team-list';

export const routes: Routes = [
  { path: '', redirectTo: 'teams', pathMatch: 'full' },
  { path: 'login', component: Login },
  { path: 'register', component: Register },
  { path: 'forgot-password', component: ForgotPassword },
  { path: 'reset-password', component: ResetPassword },
  { path: 'teams', component: TeamList, canActivate: [authGuard] },
  { path: 'teams/:id/boards/:boardId', component: TeamBoard, canActivate: [authGuard] },
  { path: 'teams/:id/board', component: TeamBoard, canActivate: [authGuard] },
  { path: 'teams/:id/reports', component: TeamReports, canActivate: [authGuard] },
  { path: 'profile', component: Profile, canActivate: [authGuard] },
  { path: 'profile/:id', component: Profile, canActivate: [authGuard] },
  { path: '**', redirectTo: 'teams' }
];
