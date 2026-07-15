import { Injectable, signal } from '@angular/core';

export type ThemeMode = 'light' | 'dark';

const THEME_KEY = 'todoo_theme';

@Injectable({ providedIn: 'root' })
export class ThemeService {
  readonly theme = signal<ThemeMode>(this.readStoredTheme());

  constructor() {
    this.apply(this.theme());
  }

  toggle(): void {
    this.setTheme(this.theme() === 'dark' ? 'light' : 'dark');
  }

  setTheme(mode: ThemeMode): void {
    this.theme.set(mode);
    localStorage.setItem(THEME_KEY, mode);
    this.apply(mode);
  }

  isDark(): boolean {
    return this.theme() === 'dark';
  }

  private apply(mode: ThemeMode): void {
    document.documentElement.setAttribute('data-theme', mode);
  }

  private readStoredTheme(): ThemeMode {
    try {
      const stored = localStorage.getItem(THEME_KEY);
      return stored === 'dark' ? 'dark' : 'light';
    } catch {
      return 'light';
    }
  }
}
