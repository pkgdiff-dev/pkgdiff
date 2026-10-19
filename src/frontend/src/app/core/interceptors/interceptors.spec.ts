import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { environment } from '../../../environments/environment';
import { AuthService } from '../services/auth.service';
import { ToastService } from '../services/toast.service';
import { authInterceptor } from './auth.interceptor';
import { errorInterceptor } from './error.interceptor';

describe('HTTP interceptors', () => {
  let http: HttpClient;
  let backend: HttpTestingController;
  let auth: { getToken: ReturnType<typeof vi.fn>; handleUnauthorized: ReturnType<typeof vi.fn> };
  let toast: ToastService;

  const api = (path: string) => `${environment.apiUrl}/${path}`;

  beforeEach(() => {
    auth = { getToken: vi.fn(() => 'token-1'), handleUnauthorized: vi.fn() };
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor, errorInterceptor])),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: auth },
      ],
    });
    http = TestBed.inject(HttpClient);
    backend = TestBed.inject(HttpTestingController);
    toast = TestBed.inject(ToastService);
  });

  afterEach(() => backend.verify());

  it('sends the token to the application API and to nobody else', () => {
    http.get(api('admin/users')).subscribe();
    http.get('https://api.github.com/rate_limit').subscribe();

    expect(backend.expectOne(api('admin/users')).request.headers.get('Authorization')).toBe('Bearer token-1');
    expect(backend.expectOne('https://api.github.com/rate_limit').request.headers.has('Authorization')).toBe(false);
  });

  it('ends the session when a request that carried the token is answered with 401', () => {
    const failures: number[] = [];
    http.get(api('admin/users')).subscribe({ error: error => failures.push(error.status) });

    backend.expectOne(api('admin/users')).flush({ error: 'expired' }, { status: 401, statusText: 'Unauthorized' });

    expect(auth.handleUnauthorized).toHaveBeenCalledTimes(1);
    // The caller still gets the failure and resets its own state
    expect(failures).toEqual([401]);
  });

  it('leaves a failed sign-in and other failures of a visitor without a token alone', () => {
    auth.getToken.mockReturnValue(null);
    http.post(api('auth/login'), {}).subscribe({ error: () => undefined });
    http.get(api('Package/registries')).subscribe({ error: () => undefined });

    const login = backend.expectOne(api('auth/login'));
    expect(login.request.headers.has('Authorization')).toBe(false);
    login.flush({ error: 'Invalid username or password' }, { status: 401, statusText: 'Unauthorized' });
    backend.expectOne(api('Package/registries')).flush({}, { status: 403, statusText: 'Forbidden' });

    expect(auth.handleUnauthorized).not.toHaveBeenCalled();
    expect(toast.toasts()).toEqual([]);
  });

  it('says once that the server cannot be reached or is limiting requests', () => {
    for (const path of ['Package/registries', 'Package/cached']) {
      http.get(api(path)).subscribe({ error: () => undefined });
      backend.expectOne(api(path)).error(new ProgressEvent('error'));
    }
    http.get(api('Package/live-updates')).subscribe({ error: () => undefined });
    backend.expectOne(api('Package/live-updates')).flush({ error: 'Rate Limit Exceeded' }, { status: 429, statusText: 'Too Many Requests' });

    expect(toast.toasts().map(t => t.message)).toEqual([
      'Cannot reach the server. Check your connection and try again.',
      'Too many requests. Please wait a moment and try again.',
    ]);
  });

  it('leaves a server error to the caller, which knows what failed', () => {
    http.get(api('blog/missing')).subscribe({ error: () => undefined });
    http.get(api('blog')).subscribe({ error: () => undefined });

    backend.expectOne(api('blog/missing')).flush({}, { status: 404, statusText: 'Not Found' });
    backend.expectOne(api('blog')).flush({}, { status: 500, statusText: 'Server Error' });

    expect(toast.toasts()).toEqual([]);
  });
});
