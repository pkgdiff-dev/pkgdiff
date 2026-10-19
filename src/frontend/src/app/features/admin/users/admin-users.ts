import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Observable } from 'rxjs';
import { LucideAngularModule, Search, Plus, Edit2, Trash2, RefreshCw, Key } from 'lucide-angular';
import { ApiService, QueryParams } from '../../../core/services/api';
import { ToastService } from '../../../core/services/toast.service';
import { ConfirmService } from '../../../core/services/confirm.service';
import { PaginatedResult } from '../../../core/models/package.models';
import { rejectionMessage } from '../../../core/utils/http-error.utils';
import { GridPatternComponent } from '../../../shared/components/grid-pattern/grid-pattern';
import { ModalComponent } from '../../../shared/components/modal/modal';
import { PaginationComponent } from '../../../shared/components/pagination/pagination';
import { HasPermissionDirective } from '../../../shared/directives/has-permission.directive';

interface AdminUserInfo {
  id: string;
  username: string;
  email: string;
  roles: string[];
  createdAt: string;
  lastLoginAt: string | null;
  emailConfirmed: boolean;
  lockoutEnabled: boolean;
  lockoutEnd: string | null;
}

interface RoleInfo {
  id: string;
  name: string;
  userCount: number;
}

interface UserForm {
  username: string;
  email: string;
  password: string;
  roles: string[];
}

type ModalMode = 'create' | 'edit' | 'password';

const MODAL_TITLES: Record<ModalMode, string> = {
  create: 'Add User',
  edit: 'Edit User',
  password: 'Reset Password',
};

const SUBMIT_LABELS: Record<ModalMode, string> = {
  create: 'Create',
  edit: 'Update',
  password: 'Reset Password',
};

@Component({
  selector: 'app-admin-users',
  standalone: true,
  imports: [FormsModule, LucideAngularModule, GridPatternComponent, ModalComponent, PaginationComponent, HasPermissionDirective],
  templateUrl: './admin-users.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminUsersComponent {
  private api = inject(ApiService);
  private toast = inject(ToastService);
  private confirmService = inject(ConfirmService);

  readonly SearchIcon = Search;
  readonly PlusIcon = Plus;
  readonly EditIcon = Edit2;
  readonly TrashIcon = Trash2;
  readonly RefreshIcon = RefreshCw;
  readonly KeyIcon = Key;

  result = signal<PaginatedResult<AdminUserInfo> | null>(null);
  availableRoles = signal<RoleInfo[]>([]);
  loading = signal(true);
  searchQuery = signal('');
  currentPage = signal(1);

  showModal = signal(false);
  saving = signal(false);
  modalMode = signal<ModalMode>('create');
  editingUser = signal<AdminUserInfo | null>(null);
  formData = signal<UserForm>({ username: '', email: '', password: '', roles: [] });

  modalTitle = computed(() => MODAL_TITLES[this.modalMode()]);
  submitLabel = computed(() => SUBMIT_LABELS[this.modalMode()]);

  constructor() {
    this.loadRoles();
    this.load();
  }

  loadRoles(): void {
    this.api.get<RoleInfo[]>('admin/roles').subscribe({
      next: (roles) => this.availableRoles.set(roles),
      error: () => this.toast.error('Failed to load roles.'),
    });
  }

  load(): void {
    this.loading.set(true);
    const params: QueryParams = { page: this.currentPage(), pageSize: 20 };
    const search = this.searchQuery().trim();
    if (search) params['search'] = search;

    this.api.get<PaginatedResult<AdminUserInfo>>('admin/users', params).subscribe({
      next: (data) => {
        this.result.set(data);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.toast.error('Failed to load users.');
      },
    });
  }

  onSearch(): void {
    this.goToPage(1);
  }

  openCreate(): void {
    this.open('create', null, { username: '', email: '', password: '', roles: [] });
  }

  openEdit(user: AdminUserInfo): void {
    this.open('edit', user, { username: user.username, email: user.email, password: '', roles: [...user.roles] });
  }

  openResetPassword(user: AdminUserInfo): void {
    this.open('password', user, { username: user.username, email: user.email, password: '', roles: [] });
  }

  closeModal(): void {
    this.showModal.set(false);
  }

  setField<K extends keyof UserForm>(field: K, value: UserForm[K]): void {
    this.formData.update(form => ({ ...form, [field]: value }));
  }

  toggleRole(roleName: string): void {
    const roles = this.formData().roles;
    this.setField('roles', roles.includes(roleName) ? roles.filter(r => r !== roleName) : [...roles, roleName]);
  }

  saveUser(): void {
    if (this.saving()) return;

    if (this.modalMode() === 'create') {
      this.createUser();
    } else if (this.modalMode() === 'edit') {
      this.updateUser();
    } else {
      this.resetUserPassword();
    }
  }

  resetUserPassword(): void {
    const user = this.editingUser();
    const form = this.formData();
    if (!user || !form.password.trim()) {
      this.toast.warning('Please enter a new password.');
      return;
    }

    this.submit(
      this.api.post(`admin/users/${user.id}/password`, { newPassword: form.password }),
      'Password reset successfully.',
      'Failed to reset password.',
      false,
    );
  }

  createUser(): void {
    const form = this.formData();
    if (!form.username.trim() || !form.email.trim() || !form.password.trim()) {
      this.toast.warning('Please fill all required fields.');
      return;
    }

    this.submit(this.api.post('admin/users', form), 'User created successfully.', 'Failed to create user.');
  }

  updateUser(): void {
    const user = this.editingUser();
    const form = this.formData();
    if (!user || !form.email.trim()) {
      this.toast.warning('Please fill all required fields.');
      return;
    }

    this.submit(
      this.api.put(`admin/users/${user.id}`, { email: form.email, roles: form.roles }),
      'User updated successfully.',
      'Failed to update user.',
    );
  }

  async deleteUser(user: AdminUserInfo): Promise<void> {
    if (user.roles.includes('Admin') && user.username === 'pkgdiff_admin') {
      this.toast.error('Cannot delete the super admin user.');
      return;
    }

    if (!await this.confirmService.confirm(`Are you sure you want to delete user "${user.username}"?`)) return;

    this.api.delete(`admin/users/${user.id}`).subscribe({
      next: () => {
        this.toast.success('User deleted successfully.');
        this.load();
      },
      error: () => this.toast.error('Failed to delete user.'),
    });
  }

  async toggleLock(user: AdminUserInfo): Promise<void> {
    const action = user.lockoutEnabled ? 'unlock' : 'lock';
    if (!await this.confirmService.confirm(`Are you sure you want to ${action} user "${user.username}"?`)) return;

    this.api.post(`admin/users/${user.id}/${action}`, {}).subscribe({
      next: () => {
        this.toast.success(`User ${action}ed successfully.`);
        this.load();
      },
      error: () => this.toast.error(`Failed to ${action} user.`),
    });
  }

  goToPage(page: number): void {
    this.currentPage.set(page);
    this.load();
  }

  formatDate(dateStr: string | null): string {
    if (!dateStr) return 'Never';
    return new Date(dateStr).toLocaleString('tr-TR', {
      day: '2-digit',
      month: '2-digit',
      year: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
    });
  }

  private open(mode: ModalMode, user: AdminUserInfo | null, form: UserForm): void {
    this.modalMode.set(mode);
    this.editingUser.set(user);
    this.formData.set(form);
    this.showModal.set(true);
  }

  private submit(request: Observable<unknown>, done: string, failed: string, reload = true): void {
    this.saving.set(true);
    request.subscribe({
      next: () => {
        this.saving.set(false);
        this.toast.success(done);
        this.closeModal();
        if (reload) this.load();
      },
      error: (error: unknown) => {
        this.saving.set(false);
        this.toast.error(rejectionMessage(error, failed));
      },
    });
  }
}
