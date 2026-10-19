import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, Router, RouterStateSnapshot, UrlTree, provideRouter } from '@angular/router';

import { routes } from '../../app.routes';
import { AuthService } from '../services/auth.service';
import { authGuard } from './auth.guard';
import { guestGuard } from './guest.guard';
import { permissionGuard } from './permission.guard';

describe('route guards', () => {
  let auth: { hasValidSession: ReturnType<typeof vi.fn>; hasPermission: ReturnType<typeof vi.fn> };
  let router: Router;

  const state = {} as RouterStateSnapshot;
  const routeWith = (data: Record<string, unknown>) => ({ data }) as unknown as ActivatedRouteSnapshot;
  const run = <T>(guard: () => T): T => TestBed.runInInjectionContext(guard);
  const urlOf = (result: unknown) => router.serializeUrl(result as UrlTree);

  beforeEach(() => {
    auth = { hasValidSession: vi.fn(() => true), hasPermission: vi.fn(() => true) };
    TestBed.configureTestingModule({
      providers: [provideRouter([]), { provide: AuthService, useValue: auth }],
    });
    router = TestBed.inject(Router);
  });

  it('lets a signed-in user into the admin area and sends everyone else to the login page', () => {
    expect(run(() => authGuard(routeWith({}), state))).toBe(true);

    auth.hasValidSession.mockReturnValue(false);
    expect(urlOf(run(() => authGuard(routeWith({}), state)))).toBe('/admin/login');
  });

  it('keeps a signed-in user away from the login page', () => {
    expect(urlOf(run(() => guestGuard(routeWith({}), state)))).toBe('/admin/dashboard');

    auth.hasValidSession.mockReturnValue(false);
    expect(run(() => guestGuard(routeWith({}), state))).toBe(true);
  });

  it('opens a route only to a user who holds its permission', () => {
    const users = routeWith({ permission: 'Permissions.Users.View' });
    expect(run(() => permissionGuard(users, state))).toBe(true);
    expect(auth.hasPermission).toHaveBeenCalledWith('Permissions.Users.View');

    auth.hasPermission.mockReturnValue(false);
    expect(urlOf(run(() => permissionGuard(users, state)))).toBe('/admin/dashboard');

    expect(run(() => permissionGuard(routeWith({}), state))).toBe(true);
  });

  it('guards every admin page except the dashboard with the permission its menu entry asks for', () => {
    const admin = routes.find(route => route.path === 'admin')!;
    const guarded = Object.fromEntries((admin.children ?? [])
      .filter(route => route.canActivate?.includes(permissionGuard))
      .map(route => [route.path, route.data?.['permission']]));

    expect(admin.canActivate).toEqual([authGuard]);
    expect(guarded).toEqual({
      'health-monitoring': 'Permissions.Health.View',
      packages: 'Permissions.Packages.View',
      jobs: 'Permissions.Jobs.View',
      registries: 'Permissions.Registries.View',
      'search-history': 'Permissions.Security.ViewLogs',
      settings: 'Permissions.Settings.View',
      'version-exclude-patterns': 'Permissions.VersionExcludePatterns.View',
      users: 'Permissions.Users.View',
      roles: 'Permissions.Roles.View',
      security: 'Permissions.Security.ManageIps',
      'scrape-logs': 'Permissions.ScrapeLogs.View',
      blogs: 'Permissions.Blog.View',
      'social-media': 'Permissions.SocialMedia.View',
      'social-media/linkedin/callback': 'Permissions.SocialMedia.Manage',
      contributors: 'Permissions.Contributors.Manage',
    });

    const unguarded = (admin.children ?? []).filter(route => !route.canActivate).map(route => route.path);
    expect(unguarded).toEqual(['', 'dashboard']);
  });
});
