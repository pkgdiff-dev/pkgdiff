import { Injectable, PLATFORM_ID, computed, inject, signal } from '@angular/core';
import { isPlatformBrowser } from '@angular/common';
import { Router } from '@angular/router';
import { ApiService } from './api';
import { BrowserStorage } from './browser-storage.service';
import { AuthUser, LoginRequest, LoginResponse } from '../models/auth.models';

// setTimeout takes a 32-bit delay; a session that outlives it is re-armed when the timer fires
const MAX_TIMER_MS = 2 ** 31 - 1;

@Injectable({ providedIn: 'root' })
export class AuthService {
  private api = inject(ApiService);
  private router = inject(Router);
  private storage = inject(BrowserStorage);
  private isBrowser = isPlatformBrowser(inject(PLATFORM_ID));
  private readonly STORAGE_KEY = 'pkgdiff-auth';
  private expiryTimer: ReturnType<typeof setTimeout> | null = null;

  readonly currentUser = signal<AuthUser | null>(null);
  // No clock is read here: the expiry timer clears the user at the moment the token expires
  readonly isAuthenticated = computed(() => this.currentUser() !== null);

  constructor() {
    const stored = this.loadFromStorage();
    if (stored) {
      this.currentUser.set(stored);
      this.armExpiry(stored.expiresAt);
    }

    // Timers stall while the machine sleeps, so the session is rechecked when the tab returns
    if (this.isBrowser) {
      document.addEventListener('visibilitychange', () => {
        if (document.visibilityState === 'visible') this.hasValidSession();
      });
    }
  }

  login(username: string, password: string) {
    const body: LoginRequest = { username, password };
    return this.api.post<LoginResponse>('auth/login', body);
  }

  setUser(response: LoginResponse): void {
    const user: AuthUser = {
      username: response.username,
      email: response.email,
      role: response.role,
      permissions: response.permissions || [],
      token: response.token,
      expiresAt: new Date(response.expiresAt),
    };
    this.currentUser.set(user);
    this.storage.set(this.STORAGE_KEY, JSON.stringify(user));
    this.armExpiry(user.expiresAt);
  }

  hasPermission(permission: string): boolean {
    const user = this.currentUser();
    if (!user) return false;
    if (user.role === 'Admin') return true;
    return (user.permissions || []).includes(permission);
  }

  hasValidSession(): boolean {
    const user = this.currentUser();
    if (!user) return false;
    if (user.expiresAt.getTime() <= Date.now()) {
      this.endSession();
      return false;
    }
    return true;
  }

  logout(): void {
    this.clear();
    this.router.navigate(['/admin/login']);
  }

  handleUnauthorized(): void {
    this.endSession();
  }

  getToken(): string | null {
    return this.hasValidSession() ? this.currentUser()!.token : null;
  }

  private endSession(): void {
    this.clear();
    if (this.router.url.startsWith('/admin')) {
      this.router.navigate(['/admin/login']);
    }
  }

  private clear(): void {
    if (this.expiryTimer) {
      clearTimeout(this.expiryTimer);
      this.expiryTimer = null;
    }
    this.currentUser.set(null);
    this.storage.remove(this.STORAGE_KEY);
  }

  private armExpiry(expiresAt: Date): void {
    if (!this.isBrowser) return;
    if (this.expiryTimer) clearTimeout(this.expiryTimer);

    const remaining = expiresAt.getTime() - Date.now();
    this.expiryTimer = setTimeout(() => {
      this.expiryTimer = null;
      if (this.hasValidSession()) this.armExpiry(expiresAt);
    }, Math.min(Math.max(remaining, 0), MAX_TIMER_MS));
  }

  private loadFromStorage(): AuthUser | null {
    const parsed = this.storage.getJson<Omit<AuthUser, 'expiresAt'> & { expiresAt: string }>(this.STORAGE_KEY);
    if (!parsed) {
      this.storage.remove(this.STORAGE_KEY);
      return null;
    }
    try {
      const user: AuthUser = { ...parsed, permissions: parsed.permissions || [], expiresAt: new Date(parsed.expiresAt) };
      // An unreadable date is an expired session too
      if (!(user.expiresAt.getTime() > Date.now())) {
        this.storage.remove(this.STORAGE_KEY);
        return null;
      }
      return user;
    } catch {
      this.storage.remove(this.STORAGE_KEY);
      return null;
    }
  }
}
