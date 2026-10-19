import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { LucideAngularModule, Shield, Plus, Trash2, RefreshCw, Key, Lock } from 'lucide-angular';
import { ApiService } from '../../../core/services/api';
import { ToastService } from '../../../core/services/toast.service';
import { ConfirmService } from '../../../core/services/confirm.service';
import { rejectionMessage } from '../../../core/utils/http-error.utils';
import { GridPatternComponent } from '../../../shared/components/grid-pattern/grid-pattern';
import { ModalComponent } from '../../../shared/components/modal/modal';
import { HasPermissionDirective } from '../../../shared/directives/has-permission.directive';

interface RoleInfo {
  id: string;
  name: string;
  userCount: number;
}

interface PermissionDto {
  name: string;
  description: string;
  group: string;
  isSelected: boolean;
}

interface RolePermissionsDto {
  roleId: string;
  roleName: string;
  permissions: PermissionDto[];
}

interface PermissionGroup {
  name: string;
  permissions: PermissionDto[];
}

@Component({
  selector: 'app-admin-roles',
  standalone: true,
  imports: [FormsModule, LucideAngularModule, GridPatternComponent, ModalComponent, HasPermissionDirective],
  templateUrl: './admin-roles.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminRolesComponent {
  private api = inject(ApiService);
  private toast = inject(ToastService);
  private confirmService = inject(ConfirmService);

  readonly ShieldIcon = Shield;
  readonly PlusIcon = Plus;
  readonly TrashIcon = Trash2;
  readonly RefreshIcon = RefreshCw;
  readonly KeyIcon = Key;
  readonly LockIcon = Lock;

  roles = signal<RoleInfo[]>([]);
  loading = signal(true);
  saving = signal(false);

  showModal = signal(false);
  roleName = signal('');

  // The role whose permissions are open in the dialog
  selectedRole = signal<RoleInfo | null>(null);
  permissionLoading = signal(false);
  rolePermissions = signal<RolePermissionsDto | null>(null);

  permissionGroups = computed<PermissionGroup[]>(() => {
    const grouped = new Map<string, PermissionDto[]>();
    for (const permission of this.rolePermissions()?.permissions ?? []) {
      const group = grouped.get(permission.group);
      if (group) {
        group.push(permission);
      } else {
        grouped.set(permission.group, [permission]);
      }
    }
    return Array.from(grouped, ([name, permissions]) => ({ name, permissions }))
      .sort((a, b) => (a.name < b.name ? -1 : a.name > b.name ? 1 : 0));
  });

  constructor() {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.api.get<RoleInfo[]>('admin/roles').subscribe({
      next: (data) => {
        this.roles.set(data);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.toast.error('Failed to load roles.');
      },
    });
  }

  openCreate(): void {
    this.roleName.set('');
    this.showModal.set(true);
  }

  closeModal(): void {
    this.showModal.set(false);
  }

  createRole(): void {
    if (this.saving()) return;
    if (!this.roleName().trim()) {
      this.toast.warning('Please enter a role name.');
      return;
    }

    this.saving.set(true);
    this.api.post('admin/roles', { name: this.roleName() }).subscribe({
      next: () => {
        this.saving.set(false);
        this.toast.success('Role created successfully.');
        this.closeModal();
        this.load();
      },
      error: (error: unknown) => {
        this.saving.set(false);
        this.toast.error(rejectionMessage(error, 'Failed to create role.'));
      },
    });
  }

  async deleteRole(role: RoleInfo): Promise<void> {
    if (role.userCount > 0) {
      this.toast.warning(`Cannot delete role "${role.name}" because it has ${role.userCount} user(s).`);
      return;
    }

    if (!await this.confirmService.confirm(`Delete role "${role.name}"?`)) return;

    this.api.delete(`admin/roles/${role.id}`).subscribe({
      next: () => {
        this.toast.success(`Role "${role.name}" deleted.`);
        this.load();
      },
      error: () => this.toast.error('Failed to delete role.'),
    });
  }

  openPermissions(role: RoleInfo): void {
    this.selectedRole.set(role);
    this.rolePermissions.set(null);
    this.loadPermissions(role);
  }

  closePermissionModal(): void {
    this.selectedRole.set(null);
    this.rolePermissions.set(null);
  }

  private loadPermissions(role: RoleInfo): void {
    // The dialog may have been closed, or opened for another role, before the answer arrives;
    // showing it then would let one role's permissions be saved onto another
    const stillOpen = () => this.selectedRole() === role;

    this.permissionLoading.set(true);
    this.api.get<RolePermissionsDto>(`admin/roles/${role.id}/permissions`).subscribe({
      next: (data) => {
        if (!stillOpen()) return;
        this.rolePermissions.set(data);
        this.permissionLoading.set(false);
      },
      error: () => {
        if (!stillOpen()) return;
        this.toast.error('Failed to load permissions.');
        this.permissionLoading.set(false);
        this.closePermissionModal();
      }
    });
  }

  togglePermission(permission: PermissionDto): void {
    this.rolePermissions.update(current => current && {
      ...current,
      permissions: current.permissions.map(p => p.name === permission.name ? { ...p, isSelected: !p.isSelected } : p),
    });
  }

  savePermissions(): void {
    const roleId = this.selectedRole()?.id;
    const permissions = this.rolePermissions();

    if (!roleId || !permissions || this.saving()) return;

    const body = {
      roleId,
      permissions: permissions.permissions.filter(p => p.isSelected).map(p => p.name),
    };

    this.saving.set(true);
    this.api.put(`admin/roles/${roleId}/permissions`, body).subscribe({
      next: () => {
        this.saving.set(false);
        this.toast.success('Permissions updated successfully.');
        this.closePermissionModal();
      },
      error: () => {
        this.saving.set(false);
        this.toast.error('Failed to update permissions.');
      }
    });
  }
}
