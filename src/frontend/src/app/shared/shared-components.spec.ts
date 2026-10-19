import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { AuthService } from '../core/services/auth.service';
import { ConfirmService } from '../core/services/confirm.service';
import { ConfirmModalComponent } from './components/confirm-modal/confirm-modal.component';
import { InputComponent } from './components/input/input.component';
import { ModalComponent } from './components/modal/modal';
import { PaginationComponent } from './components/pagination/pagination';
import { HasPermissionDirective } from './directives/has-permission.directive';

const pressEscape = () => document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));

@Component({
  standalone: true,
  imports: [ModalComponent],
  template: `
    <button id="opener" type="button">open</button>
    @if (open()) {
    <app-modal title="Edit user" [closable]="closable()" (closed)="open.set(false)">
      <input id="first" />
      <div modalFooter><button id="save" type="button">Save</button></div>
    </app-modal>
    }
  `,
})
class ModalHost {
  open = signal(false);
  closable = signal(true);
}

describe('ModalComponent', () => {
  let fixture: ComponentFixture<ModalHost>;
  const element = () => fixture.nativeElement as HTMLElement;
  const dialog = () => element().querySelector<HTMLElement>('[role="dialog"]');

  beforeEach(() => {
    fixture = TestBed.createComponent(ModalHost);
    document.body.appendChild(fixture.nativeElement);
    fixture.detectChanges();
  });

  afterEach(() => fixture.nativeElement.remove());

  it('is a modal dialog labelled by its title, with its footer content', () => {
    fixture.componentInstance.open.set(true);
    fixture.detectChanges();

    const title = element().querySelector(`#${dialog()!.getAttribute('aria-labelledby')}`);
    expect(dialog()!.getAttribute('aria-modal')).toBe('true');
    expect(title?.textContent?.trim()).toBe('Edit user');
    expect(dialog()!.querySelector('footer #save')).not.toBeNull();
    expect(dialog()!.querySelector('button[aria-label="Close dialog"]')).not.toBeNull();
  });

  it('closes on Escape, on its close button and on a click on the backdrop', () => {
    const host = fixture.componentInstance;
    const reopen = () => {
      host.open.set(true);
      fixture.detectChanges();
    };

    reopen();
    pressEscape();
    fixture.detectChanges();
    expect(dialog()).toBeNull();

    reopen();
    element().querySelector<HTMLButtonElement>('button[aria-label="Close dialog"]')!.click();
    fixture.detectChanges();
    expect(dialog()).toBeNull();

    reopen();
    element().querySelector<HTMLElement>('[aria-hidden="true"].absolute')!.click();
    fixture.detectChanges();
    expect(dialog()).toBeNull();
  });

  it('stays open while it is not closable', () => {
    fixture.componentInstance.closable.set(false);
    fixture.componentInstance.open.set(true);
    fixture.detectChanges();

    pressEscape();
    element().querySelector<HTMLElement>('[aria-hidden="true"].absolute')!.click();
    fixture.detectChanges();

    expect(dialog()).not.toBeNull();
    expect(element().querySelector<HTMLButtonElement>('button[aria-label="Close dialog"]')!.disabled).toBe(true);
  });
});

describe('PaginationComponent', () => {
  let fixture: ComponentFixture<PaginationComponent>;
  let pages: number[];

  function render(page: number, totalPages: number, total: number | null = null): HTMLElement {
    fixture = TestBed.createComponent(PaginationComponent);
    fixture.componentRef.setInput('page', page);
    fixture.componentRef.setInput('totalPages', totalPages);
    fixture.componentRef.setInput('total', total);
    pages = [];
    fixture.componentInstance.pageChange.subscribe(value => pages.push(value));
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  const button = (root: HTMLElement, label: string) => root.querySelector<HTMLButtonElement>(`button[aria-label="${label}"]`)!;

  it('renders nothing for a single page', () => {
    expect(render(1, 1).querySelector('nav')).toBeNull();
    expect(render(1, 0).querySelector('nav')).toBeNull();
  });

  it('says where the list is and asks for the neighbouring pages', () => {
    const root = render(2, 5, 93);

    expect(root.querySelector('nav')!.getAttribute('aria-label')).toBe('Pagination');
    expect(root.textContent?.replace(/\s+/g, ' ')).toContain('Page 2 of 5 (93 total)');

    button(root, 'Previous page').click();
    button(root, 'Next page').click();
    expect(pages).toEqual([1, 3]);
  });

  it('does not leave the list at either end', () => {
    const first = render(1, 3);
    expect(button(first, 'Previous page').disabled).toBe(true);
    expect(button(first, 'Next page').disabled).toBe(false);

    const last = render(3, 3);
    expect(button(last, 'Next page').disabled).toBe(true);
    fixture.componentInstance.go(4);
    fixture.componentInstance.go(3);
    expect(pages).toEqual([]);
  });
});

describe('ConfirmModalComponent', () => {
  it('asks in an alert dialog and takes Escape as "no"', async () => {
    const fixture = TestBed.createComponent(ConfirmModalComponent);
    const confirm = TestBed.inject(ConfirmService);
    const root = fixture.nativeElement as HTMLElement;

    const refused = confirm.confirm('Delete the user?', 'Are you sure?');
    fixture.detectChanges();
    const dialog = root.querySelector('[role="alertdialog"]')!;
    expect(root.querySelector(`#${dialog.getAttribute('aria-labelledby')}`)?.textContent?.trim()).toBe('Are you sure?');
    expect(root.querySelector(`#${dialog.getAttribute('aria-describedby')}`)?.textContent?.trim()).toBe('Delete the user?');

    pressEscape();
    expect(await refused).toBe(false);
    fixture.detectChanges();
    expect(root.querySelector('[role="alertdialog"]')).toBeNull();

    const accepted = confirm.confirm('Delete the user?');
    fixture.detectChanges();
    Array.from(root.querySelectorAll('button')).find(button => button.textContent?.trim() === 'Confirm')!.click();
    expect(await accepted).toBe(true);

    // Escape with no question open answers nothing
    pressEscape();
  });
});

describe('InputComponent', () => {
  it('shows a value written by the forms API and reports what is typed', () => {
    const fixture = TestBed.createComponent(InputComponent);
    const component = fixture.componentInstance;
    const typed: string[] = [];
    component.registerOnChange(value => typed.push(value));
    fixture.detectChanges();
    const input = (fixture.nativeElement as HTMLElement).querySelector('input')!;

    component.writeValue('admin');
    component.setDisabledState(true);
    fixture.detectChanges();
    expect(input.value).toBe('admin');
    expect(input.disabled).toBe(true);

    component.writeValue(null);
    fixture.detectChanges();
    expect(input.value).toBe('');

    input.value = 'viewer';
    input.dispatchEvent(new Event('input'));
    expect(typed).toEqual(['viewer']);
    expect(component.value()).toBe('viewer');
  });

  it('shows and hides a password on request, and says which', () => {
    const fixture = TestBed.createComponent(InputComponent);
    fixture.componentRef.setInput('type', 'password');
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    const toggle = root.querySelector('button')!;

    expect(root.querySelector('input')!.type).toBe('password');
    expect(toggle.getAttribute('aria-label')).toBe('Show password');

    toggle.click();
    fixture.detectChanges();

    expect(root.querySelector('input')!.type).toBe('text');
    expect(toggle.getAttribute('aria-label')).toBe('Hide password');
    expect(toggle.getAttribute('aria-pressed')).toBe('true');
  });
});

@Component({
  standalone: true,
  imports: [HasPermissionDirective],
  template: `<button *appHasPermission="permission()" type="button">Delete</button>`,
})
class PermissionHost {
  permission = signal('Permissions.Users.Delete');
}

describe('HasPermissionDirective', () => {
  it('follows the signed-in user and the permission it is asked for', () => {
    const user = signal<{ permissions: string[] } | null>(null);
    TestBed.configureTestingModule({
      providers: [{
        provide: AuthService,
        useValue: { currentUser: user, hasPermission: (permission: string) => user()?.permissions.includes(permission) ?? false },
      }],
    });
    const fixture = TestBed.createComponent(PermissionHost);
    const shown = () => (fixture.nativeElement as HTMLElement).querySelectorAll('button').length;

    fixture.detectChanges();
    expect(shown()).toBe(0);

    user.set({ permissions: ['Permissions.Users.Delete'] });
    fixture.detectChanges();
    expect(shown()).toBe(1);

    fixture.componentInstance.permission.set('Permissions.Roles.Delete');
    fixture.detectChanges();
    expect(shown()).toBe(0);

    user.set({ permissions: ['Permissions.Roles.Delete'] });
    fixture.detectChanges();
    expect(shown()).toBe(1);

    user.set(null);
    fixture.detectChanges();
    expect(shown()).toBe(0);
  });
});
