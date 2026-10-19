import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { LucideAngularModule, History, Trash2 } from 'lucide-angular';
import { ApiService } from '../../../core/services/api';
import { ToastService } from '../../../core/services/toast.service';
import { ConfirmService } from '../../../core/services/confirm.service';
import { PaginatedResult } from '../../../core/models/package.models';
import { GridPatternComponent } from '../../../shared/components/grid-pattern/grid-pattern';
import { PaginationComponent } from '../../../shared/components/pagination/pagination';
import { HasPermissionDirective } from '../../../shared/directives/has-permission.directive';

interface RecentSearch {
  id: number;
  packageId: string;
  versionRange: string;
  searchedAt: string;
  processingTime: string;
  ipAddress: string;
}

@Component({
  selector: 'app-admin-search-history',
  standalone: true,
  imports: [LucideAngularModule, GridPatternComponent, PaginationComponent, HasPermissionDirective],
  templateUrl: './admin-search-history.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminSearchHistoryComponent {
  private api = inject(ApiService);
  private toast = inject(ToastService);
  private confirmService = inject(ConfirmService);

  readonly HistoryIcon = History;
  readonly TrashIcon = Trash2;

  result = signal<PaginatedResult<RecentSearch> | null>(null);
  loading = signal(true);
  currentPage = signal(1);

  constructor() {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.api.get<PaginatedResult<RecentSearch>>('admin/search-history', { page: this.currentPage(), pageSize: 20 }).subscribe({
      next: (data) => {
        this.result.set(data);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.toast.error('Failed to load search history.');
      },
    });
  }

  async clearAll(): Promise<void> {
    if (!await this.confirmService.confirm('This action cannot be undone.', 'Clear all search history?')) return;
    this.api.delete('admin/search-history').subscribe({
      next: () => {
        this.toast.success('Search history cleared.');
        this.goToPage(1);
      },
      error: () => this.toast.error('Failed to clear search history.'),
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

  formatTime(time: string): string {
    const match = time?.match(/(\d+):(\d+):(\d+)/);
    if (!match) return time || '-';
    const [, h, m, s] = match;
    if (h !== '00') return `${h}h ${m}m`;
    if (m !== '00') return `${m}m ${s}s`;
    return `${s}s`;
  }
}
