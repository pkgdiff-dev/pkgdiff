import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Observable, Subject } from 'rxjs';

import { ApiService, QueryParams } from '../../../core/services/api';
import { AuthService } from '../../../core/services/auth.service';
import { ToastService } from '../../../core/services/toast.service';
import { AdminUsersComponent } from './admin-users';

interface ApiCall {
  method: 'get' | 'post' | 'put' | 'delete';
  path: string;
  params?: QueryParams;
  body?: unknown;
  response: Subject<unknown>;
}

const ada = {
  id: 'u1',
  username: 'ada',
  email: 'ada@example.com',
  roles: ['Editor'],
  createdAt: '2026-01-05T10:00:00Z',
  lastLoginAt: null,
  emailConfirmed: true,
  lockoutEnabled: false,
  lockoutEnd: null,
};

const pageOf = (page: number, totalPages: number) =>
  ({ data: [ada], page, pageSize: 20, total: totalPages * 20, totalPages });

describe('AdminUsersComponent', () => {
  let calls: ApiCall[];
  let fixture: ComponentFixture<AdminUsersComponent>;
  let page: AdminUsersComponent;

  const element = () => fixture.nativeElement as HTMLElement;
  const callsTo = (method: ApiCall['method'], path: string) => calls.filter(c => c.method === method && c.path === path);
  const lastCallTo = (method: ApiCall['method'], path: string) => callsTo(method, path).at(-1)!;
  const errorToasts = () => TestBed.inject(ToastService).toasts().filter(t => t.type === 'error').map(t => t.message);

  const click = async (selector: string) => {
    element().querySelector<HTMLButtonElement>(selector)!.click();
    fixture.detectChanges();
    // ngModel writes a control's value a moment after the control is created
    await fixture.whenStable();
  };

  const buttonNamed = (name: string) =>
    Array.from(element().querySelectorAll<HTMLButtonElement>('app-modal button')).find(b => b.textContent?.trim() === name)!;

  const showUsers = async (current = 1, totalPages = 1) => {
    lastCallTo('get', 'admin/users').response.next(pageOf(current, totalPages));
    fixture.detectChanges();
    await fixture.whenStable();
  };

  beforeEach(() => {
    calls = [];
    const record = <T>(call: Omit<ApiCall, 'response'>): Observable<T> => {
      const response = new Subject<T>();
      calls.push({ ...call, response: response as Subject<unknown> });
      return response;
    };
    const api: Partial<ApiService> = {
      get: <T>(path: string, params?: QueryParams) => record<T>({ method: 'get', path, params }),
      post: <T>(path: string, body: unknown) => record<T>({ method: 'post', path, body }),
      put: <T>(path: string, body: unknown) => record<T>({ method: 'put', path, body }),
      delete: <T>(path: string) => record<T>({ method: 'delete', path }),
    };

    TestBed.configureTestingModule({
      providers: [
        { provide: ApiService, useValue: api },
        { provide: AuthService, useValue: { currentUser: signal(null), hasPermission: () => true } },
      ],
    });
    fixture = TestBed.createComponent(AdminUsersComponent);
    page = fixture.componentInstance;
    fixture.detectChanges();

    callsTo('get', 'admin/roles')[0].response.next([
      { id: 'r1', name: 'Admin', userCount: 1 },
      { id: 'r2', name: 'Editor', userCount: 1 },
    ]);
  });

  it('opens the edit dialog with the data of the user and removes it when it is closed', async () => {
    await showUsers();
    expect(element().querySelector('app-modal')).toBeNull();

    await click('button[aria-label="Edit"]');

    const dialog = element().querySelector('app-modal [role="dialog"]')!;
    expect(dialog.querySelector('h2')?.textContent?.trim()).toBe('Edit User');
    expect(dialog.querySelector<HTMLInputElement>('#user-email')?.value).toBe('ada@example.com');
    expect(dialog.querySelector('label[for="user-email"]')?.textContent).toBe('Email');
    expect(dialog.querySelector('#user-username')).toBeNull();
    expect(Array.from(dialog.querySelectorAll('[aria-pressed="true"]')).map(b => b.textContent?.trim())).toEqual(['Editor']);

    await click('app-modal button[aria-label="Close dialog"]');

    expect(element().querySelector('app-modal')).toBeNull();
    expect(page.showModal()).toBe(false);
  });

  it('requests the page that was asked for, and the first page again for a new search', async () => {
    expect(lastCallTo('get', 'admin/users').params).toEqual({ page: 1, pageSize: 20 });
    await showUsers(1, 3);

    await click('button[aria-label="Next page"]');

    expect(page.loading()).toBe(true);
    expect(lastCallTo('get', 'admin/users').params).toEqual({ page: 2, pageSize: 20 });
    await showUsers(2, 3);
    expect(element().querySelector('app-pagination')?.textContent).toContain('Page 2 of 3');

    page.searchQuery.set('  ada ');
    page.onSearch();

    expect(lastCallTo('get', 'admin/users').params).toEqual({ page: 1, pageSize: 20, search: 'ada' });
  });

  it('shows why a save failed, stops saving and keeps the dialog for another try', async () => {
    await showUsers();
    await click('button[aria-label="Edit"]');
    page.setField('email', 'ada@pkgdiff.dev');

    buttonNamed('Update').click();
    fixture.detectChanges();

    const save = lastCallTo('put', 'admin/users/u1');
    expect(save.body).toEqual({ email: 'ada@pkgdiff.dev', roles: ['Editor'] });
    expect(page.saving()).toBe(true);
    expect(buttonNamed('Update').disabled).toBe(true);
    expect(element().querySelector<HTMLButtonElement>('app-modal button[aria-label="Close dialog"]')?.disabled).toBe(true);
    buttonNamed('Update').click();
    expect(callsTo('put', 'admin/users/u1')).toHaveLength(1);

    save.response.error(new HttpErrorResponse({ status: 500 }));
    fixture.detectChanges();

    expect(page.saving()).toBe(false);
    expect(errorToasts()).toEqual(['Failed to update user.']);
    expect(element().querySelector('app-modal')).not.toBeNull();
    expect(buttonNamed('Update').disabled).toBe(false);
    expect(callsTo('get', 'admin/users')).toHaveLength(1);

    buttonNamed('Update').click();
    lastCallTo('put', 'admin/users/u1').response.next({});
    fixture.detectChanges();

    expect(page.saving()).toBe(false);
    expect(element().querySelector('app-modal')).toBeNull();
    expect(callsTo('get', 'admin/users')).toHaveLength(2);
  });

  it('says why the server turned a new user down', async () => {
    await showUsers();
    page.openCreate();
    page.setField('username', 'grace');
    page.setField('email', 'grace@pkgdiff.dev');
    page.setField('password', 'short');

    page.saveUser();
    lastCallTo('post', 'admin/users').response.error(
      new HttpErrorResponse({ status: 400, error: { error: 'Passwords must be at least 12 characters.' } }));

    expect(errorToasts()).toEqual(['Passwords must be at least 12 characters.']);
    expect(page.showModal()).toBe(true);
  });
});
