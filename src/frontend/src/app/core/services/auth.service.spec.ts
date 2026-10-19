import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';

import { AuthService } from './auth.service';
import { LoginResponse } from '../models/auth.models';

const STORAGE_KEY = 'pkgdiff-auth';
const NOW = new Date('2026-10-04T12:00:00Z');

function loginResponse(expiresInMs: number, overrides: Partial<LoginResponse> = {}): LoginResponse {
  return {
    token: 'token-1',
    username: 'viewer',
    email: 'viewer@example.test',
    role: 'Viewer',
    permissions: ['Permissions.Users.View'],
    expiresAt: new Date(NOW.getTime() + expiresInMs).toISOString(),
    ...overrides,
  };
}

describe('AuthService', () => {
  let router: { url: string; navigate: ReturnType<typeof vi.fn> };

  function createService(): AuthService {
    TestBed.configureTestingModule({ providers: [{ provide: Router, useValue: router }] });
    return TestBed.inject(AuthService);
  }

  beforeEach(() => {
    vi.useFakeTimers();
    vi.setSystemTime(NOW);
    localStorage.clear();
    router = { url: '/admin/users', navigate: vi.fn() };
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('stops being authenticated at the moment the token expires', () => {
    const auth = createService();
    auth.setUser(loginResponse(60_000));

    vi.advanceTimersByTime(59_999);
    expect(auth.isAuthenticated()).toBe(true);
    expect(auth.getToken()).toBe('token-1');

    vi.advanceTimersByTime(1);
    expect(auth.isAuthenticated()).toBe(false);
    expect(auth.currentUser()).toBeNull();
    expect(localStorage.getItem(STORAGE_KEY)).toBeNull();
    expect(router.navigate).toHaveBeenCalledWith(['/admin/login']);
  });

  it('leaves a visitor on a public page where they are when the session ends', () => {
    router.url = '/package/npm/left-pad';
    const auth = createService();
    auth.setUser(loginResponse(1_000));

    vi.advanceTimersByTime(1_000);

    expect(auth.isAuthenticated()).toBe(false);
    expect(router.navigate).not.toHaveBeenCalled();
  });

  it('gives no token and does not navigate when a public page asks after the expiry', () => {
    router.url = '/';
    const auth = createService();
    auth.setUser(loginResponse(5_000));

    // The timer has not fired, as after the machine was asleep
    vi.setSystemTime(new Date(NOW.getTime() + 10_000));

    expect(auth.getToken()).toBeNull();
    expect(auth.isAuthenticated()).toBe(false);
    expect(router.navigate).not.toHaveBeenCalled();
  });

  it('restores a stored session that is still valid and discards one that is not', () => {
    localStorage.setItem(STORAGE_KEY, JSON.stringify({ ...loginResponse(60_000), permissions: undefined }));
    const restored = createService();
    expect(restored.isAuthenticated()).toBe(true);
    expect(restored.currentUser()?.permissions).toEqual([]);
    expect(restored.currentUser()?.expiresAt).toBeInstanceOf(Date);

    TestBed.resetTestingModule();
    localStorage.setItem(STORAGE_KEY, JSON.stringify(loginResponse(-1)));
    expect(createService().isAuthenticated()).toBe(false);
    expect(localStorage.getItem(STORAGE_KEY)).toBeNull();

    TestBed.resetTestingModule();
    localStorage.setItem(STORAGE_KEY, '{not json');
    expect(createService().isAuthenticated()).toBe(false);
    expect(localStorage.getItem(STORAGE_KEY)).toBeNull();
  });

  it('ends the session when the server refuses the token', () => {
    const auth = createService();
    auth.setUser(loginResponse(60_000));

    auth.handleUnauthorized();

    expect(auth.isAuthenticated()).toBe(false);
    expect(router.navigate).toHaveBeenCalledWith(['/admin/login']);

    // The old expiry timer is gone with the session
    vi.advanceTimersByTime(120_000);
    expect(router.navigate).toHaveBeenCalledTimes(1);
  });

  it('always sends a user who signs out to the login page', () => {
    router.url = '/';
    const auth = createService();
    auth.setUser(loginResponse(60_000));

    auth.logout();

    expect(auth.isAuthenticated()).toBe(false);
    expect(router.navigate).toHaveBeenCalledWith(['/admin/login']);
  });

  it('gives the Admin role every permission and everyone else only their own', () => {
    const auth = createService();
    expect(auth.hasPermission('Permissions.Users.View')).toBe(false);

    auth.setUser(loginResponse(60_000));
    expect(auth.hasPermission('Permissions.Users.View')).toBe(true);
    expect(auth.hasPermission('Permissions.Users.Delete')).toBe(false);

    auth.setUser(loginResponse(60_000, { role: 'Admin', permissions: [] }));
    expect(auth.hasPermission('Permissions.Users.Delete')).toBe(true);
  });
});
