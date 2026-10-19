import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { LucideAngularModule, Package, Search, Trash2 } from 'lucide-angular';
import { ApiService, QueryParams } from '../../../core/services/api';
import { ToastService } from '../../../core/services/toast.service';
import { ConfirmService } from '../../../core/services/confirm.service';
import { PaginatedResult } from '../../../core/models/package.models';
import { GridPatternComponent } from '../../../shared/components/grid-pattern/grid-pattern';
import { PaginationComponent } from '../../../shared/components/pagination/pagination';
import { HasPermissionDirective } from '../../../shared/directives/has-permission.directive';

interface AdminPackageInfo {
  id: number;
  packageId: string;
  latestVersion: string;
  releaseNotesSource: string | null;
  releaseNotesCount: number;
  cachedAt: string;
  expiresAt: string;
}

@Component({
  selector: 'app-admin-packages',
  standalone: true,
  imports: [FormsModule, LucideAngularModule, GridPatternComponent, PaginationComponent, HasPermissionDirective],
  templateUrl: './admin-packages.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminPackagesComponent {
  private api = inject(ApiService);
  private toast = inject(ToastService);
  private confirmService = inject(ConfirmService);

  readonly PackageIcon = Package;
  readonly SearchIcon = Search;
  readonly TrashIcon = Trash2;

  result = signal<PaginatedResult<AdminPackageInfo> | null>(null);
  loading = signal(true);
  searchQuery = signal('');
  currentPage = signal(1);

  constructor() {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    const params: QueryParams = { page: this.currentPage(), pageSize: 20 };
    const search = this.searchQuery().trim();
    if (search) params['search'] = search;

    this.api.get<PaginatedResult<AdminPackageInfo>>('admin/packages', params).subscribe({
      next: (data) => {
        this.result.set(data);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.toast.error('Failed to load packages.');
      },
    });
  }

  onSearch(): void {
    this.currentPage.set(1);
    this.load();
  }

  async deletePackage(pkg: AdminPackageInfo): Promise<void> {
    if (!await this.confirmService.confirm('This will also remove all its release notes.', `Delete cached package "${pkg.packageId}"?`)) return;
    this.api.delete(`admin/packages/${pkg.id}`).subscribe({
      next: () => {
        this.toast.success(`Package "${pkg.packageId}" deleted.`);
        this.load();
      },
      error: () => this.toast.error('Failed to delete package.'),
    });
  }

  goToPage(page: number): void {
    this.currentPage.set(page);
    this.load();
  }

  formatDate(dateStr: string): string {
    return new Date(dateStr).toLocaleString('tr-TR', {
      day: '2-digit',
      month: '2-digit',
      year: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
    });
  }

  isExpired(dateStr: string): boolean {
    return new Date(dateStr) < new Date();
  }
}
