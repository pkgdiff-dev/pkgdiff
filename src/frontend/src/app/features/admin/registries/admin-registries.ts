import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { LucideAngularModule, Globe, Plus, Pencil, Trash2, X, Check, Loader2, GripVertical } from 'lucide-angular';
import { DragDropModule, CdkDragDrop, moveItemInArray } from '@angular/cdk/drag-drop';
import { ApiService } from '../../../core/services/api';
import { ToastService } from '../../../core/services/toast.service';
import { ConfirmService } from '../../../core/services/confirm.service';
import { GridPatternComponent } from '../../../shared/components/grid-pattern/grid-pattern';
import { HasPermissionDirective } from '../../../shared/directives/has-permission.directive';

interface AdminRegistryInfo {
  id: number;
  value: string;
  label: string;
  icon: string;
  disabled: boolean;
  displayOrder: number;
  createdAt: string;
}

interface RegistryForm {
  value: string;
  label: string;
  icon: string;
  disabled: boolean;
  displayOrder: number;
}

@Component({
  selector: 'app-admin-registries',
  standalone: true,
  imports: [FormsModule, LucideAngularModule, GridPatternComponent, HasPermissionDirective, DragDropModule],
  templateUrl: './admin-registries.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminRegistriesComponent {
  private api = inject(ApiService);
  private toast = inject(ToastService);
  private confirmService = inject(ConfirmService);

  readonly GlobeIcon = Globe;
  readonly PlusIcon = Plus;
  readonly PencilIcon = Pencil;
  readonly TrashIcon = Trash2;
  readonly XIcon = X;
  readonly CheckIcon = Check;
  readonly LoaderIcon = Loader2;
  readonly GripIcon = GripVertical;

  registries = signal<AdminRegistryInfo[]>([]);
  loading = signal(true);
  saving = signal(false);

  showForm = signal(false);
  editingId = signal<number | null>(null);
  form = signal<RegistryForm>({ value: '', label: '', icon: '', disabled: false, displayOrder: 0 });

  constructor() {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.api.get<AdminRegistryInfo[]>('admin/registries').subscribe({
      next: (data) => {
        this.registries.set(data);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.toast.error('Failed to load registries.');
      },
    });
  }

  openCreate(): void {
    this.form.set({ value: '', label: '', icon: '', disabled: false, displayOrder: this.registries().length });
    this.editingId.set(null);
    this.showForm.set(true);
  }

  openEdit(reg: AdminRegistryInfo): void {
    this.form.set({ value: reg.value, label: reg.label, icon: reg.icon, disabled: reg.disabled, displayOrder: reg.displayOrder });
    this.editingId.set(reg.id);
    this.showForm.set(true);
  }

  cancelForm(): void {
    this.showForm.set(false);
    this.editingId.set(null);
  }

  setField<K extends keyof RegistryForm>(field: K, value: RegistryForm[K]): void {
    this.form.update(form => ({ ...form, [field]: value }));
  }

  saveForm(): void {
    const form = this.form();
    if (!form.value.trim() || !form.label.trim()) {
      this.toast.warning('Value and Label are required.');
      return;
    }

    this.saving.set(true);
    const id = this.editingId();

    if (id !== null) {
      this.api.put<AdminRegistryInfo>(`admin/registries/${id}`, form).subscribe({
        next: () => {
          this.toast.success('Registry updated.');
          this.showForm.set(false);
          this.saving.set(false);
          this.load();
        },
        error: () => {
          this.saving.set(false);
          this.toast.error('Failed to update registry.');
        },
      });
    } else {
      this.api.post<AdminRegistryInfo>('admin/registries', form).subscribe({
        next: () => {
          this.toast.success('Registry created.');
          this.showForm.set(false);
          this.saving.set(false);
          this.load();
        },
        error: () => {
          this.saving.set(false);
          this.toast.error('Failed to create registry.');
        },
      });
    }
  }

  async deleteRegistry(reg: AdminRegistryInfo): Promise<void> {
    if (!await this.confirmService.confirm(`Delete registry "${reg.label}"?`)) return;
    this.api.delete(`admin/registries/${reg.id}`).subscribe({
      next: () => {
        this.toast.success(`Registry "${reg.label}" deleted.`);
        this.load();
      },
      error: () => this.toast.error('Failed to delete registry.'),
    });
  }

  drop(event: CdkDragDrop<AdminRegistryInfo[]>): void {
    const moved = [...this.registries()];
    moveItemInArray(moved, event.previousIndex, event.currentIndex);

    // Shown in the new order right away; the server's list comes back if it refuses
    const list = moved.map((reg, index) => ({ ...reg, displayOrder: index }));
    this.registries.set(list);

    const payload = list.map(reg => ({ id: reg.id, displayOrder: reg.displayOrder }));
    this.api.post('admin/registries/reorder', payload).subscribe({
      next: () => {
        this.toast.success('Registry order updated.');
      },
      error: () => {
        this.toast.error('Failed to update registry order.');
        this.load();
      }
    });
  }
}
