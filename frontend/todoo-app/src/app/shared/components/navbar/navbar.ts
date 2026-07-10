import { Component, inject } from '@angular/core';
import { Router, RouterLink, RouterLinkActive } from '@angular/router';
import { AuthService } from '../../../core/services/auth.service';

@Component({
  selector: 'app-navbar',
  imports: [RouterLink, RouterLinkActive],
  templateUrl: './navbar.html',
  styleUrl: './navbar.scss'
})
export class Navbar {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  readonly user = this.auth.getUser();

  logout(): void {
    this.auth.logout();
    void this.router.navigate(['/login']);
  }
}
