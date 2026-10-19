import { Injectable, PLATFORM_ID, inject, signal } from '@angular/core';
import { DOCUMENT, isPlatformBrowser } from '@angular/common';
import { BrowserStorage } from './browser-storage.service';

export type Theme = 'light' | 'dark';

@Injectable({ providedIn: 'root' })
export class ThemeService {
  private readonly STORAGE_KEY = 'pkgdiff-theme';
  private storage = inject(BrowserStorage);
  private document = inject(DOCUMENT);
  private isBrowser = isPlatformBrowser(inject(PLATFORM_ID));

  readonly theme = signal<Theme>(this.getInitialTheme());

  constructor() {
    // The server does not know the visitor's theme and leaves the class to the inline script in
    // index.html, which sets it before the first paint
    if (this.isBrowser) {
      this.applyTheme(this.theme());
    }
  }

  toggleTheme(): void {
    const next: Theme = this.theme() === 'light' ? 'dark' : 'light';
    this.theme.set(next);
    this.applyTheme(next);
    this.storage.set(this.STORAGE_KEY, next);
  }

  private getInitialTheme(): Theme {
    const stored = this.storage.get(this.STORAGE_KEY);
    if (stored === 'light' || stored === 'dark') return stored;
    if (!this.isBrowser) return 'light';
    return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
  }

  private applyTheme(theme: Theme): void {
    const root = this.document.documentElement;
    root.classList.remove('light', 'dark');
    root.classList.add(theme);
  }
}
