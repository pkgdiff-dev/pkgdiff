import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { NgClass } from '@angular/common';
import { LucideAngularModule, Settings, Save, Copy, Check, Lock, ChevronDown } from 'lucide-angular';
import { ApiService } from '../../../core/services/api';
import { ToastService } from '../../../core/services/toast.service';
import { ConfirmService } from '../../../core/services/confirm.service';
import { GridPatternComponent } from '../../../shared/components/grid-pattern/grid-pattern';
import { HasPermissionDirective } from '../../../shared/directives/has-permission.directive';
import { ButtonComponent } from '../../../shared/components/button/button.component';
import { InputComponent } from '../../../shared/components/input/input.component';

interface SystemSettingDto {
  id: number;
  key: string;
  value: string;
  description: string;
  dataType: string;
  category: string;
  isSecret: boolean;
  hasValue: boolean;
}

interface UpdateSettingRequest {
  value: string;
  clear?: boolean;
}

interface CategoryGroup {
  name: string;
  settings: SystemSettingDto[];
}

const COPIED_MARK_MS = 2000;

// A secret is write-only: its field starts empty and whatever is typed replaces the stored value
const editableValue = (setting: SystemSettingDto): string => setting.isSecret ? '' : setting.value;

@Component({
  selector: 'app-admin-settings',
  standalone: true,
  imports: [FormsModule, LucideAngularModule, GridPatternComponent, HasPermissionDirective, ButtonComponent, InputComponent, NgClass],
  templateUrl: './admin-settings.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminSettingsComponent {
  private api = inject(ApiService);
  private toast = inject(ToastService);
  private confirmService = inject(ConfirmService);
  private copiedTimers = new Map<number, ReturnType<typeof setTimeout>>();

  readonly SettingsIcon = Settings;
  readonly SaveIcon = Save;
  readonly CopyIcon = Copy;
  readonly CheckIcon = Check;
  readonly LockIcon = Lock;
  readonly ChevronIcon = ChevronDown;

  settings = signal<SystemSettingDto[]>([]);
  loading = signal(true);
  savingId = signal<number | null>(null);

  editedValues = signal<Record<number, string>>({});
  collapsedCategories = signal<Record<string, boolean>>({});
  // Settings whose value was just copied show a check mark for a moment
  copiedIds = signal<Record<number, boolean>>({});

  categoryGroups = computed<CategoryGroup[]>(() => {
    const map = new Map<string, SystemSettingDto[]>();
    for (const s of this.settings()) {
      const cat = s.category || 'General';
      if (!map.has(cat)) map.set(cat, []);
      map.get(cat)!.push(s);
    }
    return Array.from(map.entries()).map(([name, settings]) => ({ name, settings }));
  });

  constructor() {
    this.load();

    inject(DestroyRef).onDestroy(() => this.copiedTimers.forEach(timer => clearTimeout(timer)));
  }

  load(): void {
    this.loading.set(true);
    this.api.get<SystemSettingDto[]>('admin/settings').subscribe({
      next: (data) => {
        this.settings.set(data);
        this.editedValues.set(Object.fromEntries(data.map(s => [s.id, editableValue(s)])));
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.toast.error('Failed to load settings.');
      },
    });
  }

  setEdited(id: number, value: string): void {
    this.editedValues.update(values => ({ ...values, [id]: value }));
  }

  toggleBool(id: number): void {
    this.setEdited(id, this.editedValues()[id] === 'true' ? 'false' : 'true');
  }

  saveSetting(setting: SystemSettingDto): void {
    if (!this.isChanged(setting)) {
      this.toast.warning('No changes to save.');
      return;
    }

    const body: UpdateSettingRequest = { value: this.editedValues()[setting.id] ?? '' };
    this.savingId.set(setting.id);
    this.api.put<SystemSettingDto>(`admin/settings/${setting.id}`, body).subscribe({
      next: (updated) => {
        this.savingId.set(null);
        this.applyUpdated(updated);
        this.toast.success(`Setting "${setting.key}" updated.`);
      },
      error: () => {
        this.savingId.set(null);
        this.toast.error('Failed to update setting.');
      },
    });
  }

  async clearSecret(setting: SystemSettingDto): Promise<void> {
    const confirmed = await this.confirmService.confirm(
      'Remove the stored value for ' + setting.key + '?',
      'Clear secret',
    );
    if (!confirmed) return;

    const body: UpdateSettingRequest = { value: '', clear: true };
    this.savingId.set(setting.id);
    this.api.put<SystemSettingDto>(`admin/settings/${setting.id}`, body).subscribe({
      next: (updated) => {
        this.savingId.set(null);
        this.applyUpdated(updated);
        this.toast.success(`Setting "${setting.key}" cleared.`);
      },
      error: () => {
        this.savingId.set(null);
        this.toast.error('Failed to clear setting.');
      },
    });
  }

  isChanged(setting: SystemSettingDto): boolean {
    const edited = this.editedValues()[setting.id] ?? '';
    // Secrets are write-only, so any typed value is a replacement
    return setting.isSecret ? edited.length > 0 : edited !== setting.value;
  }

  async copyValue(setting: SystemSettingDto): Promise<void> {
    if (setting.isSecret) return;

    try {
      await navigator.clipboard.writeText(this.editedValues()[setting.id] ?? '');
    } catch {
      this.toast.error('Could not copy to the clipboard.');
      return;
    }

    this.copiedIds.update(ids => ({ ...ids, [setting.id]: true }));
    this.toast.success('Copied to clipboard!');

    clearTimeout(this.copiedTimers.get(setting.id));
    this.copiedTimers.set(setting.id, setTimeout(() => {
      this.copiedTimers.delete(setting.id);
      this.copiedIds.update(ids => ({ ...ids, [setting.id]: false }));
    }, COPIED_MARK_MS));
  }

  toggleCategory(name: string): void {
    this.collapsedCategories.update(collapsed => ({ ...collapsed, [name]: !collapsed[name] }));
  }

  isCollapsed(name: string): boolean {
    return !!this.collapsedCategories()[name];
  }

  getCategoryIcon(category: string): string {
    if (category.toLowerCase().includes('twitter')) return '🐦';
    if (category.toLowerCase().includes('linkedin')) return '💼';
    if (category.toLowerCase().includes('social')) return '📣';
    if (category.toLowerCase().includes('autopilot')) return '🤖';
    if (category.toLowerCase().includes('cache')) return '⚡';
    return '⚙️';
  }

  private applyUpdated(updated: SystemSettingDto): void {
    this.settings.update(list =>
      list.map(s => s.id === updated.id ? updated : s)
    );
    this.setEdited(updated.id, editableValue(updated));
  }
}
