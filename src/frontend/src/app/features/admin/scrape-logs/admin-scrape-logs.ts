import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { LucideAngularModule, Bug, Trash2, RefreshCw, ExternalLink, Package, AlertTriangle } from 'lucide-angular';
import { ApiService, QueryParams } from '../../../core/services/api';
import { ToastService } from '../../../core/services/toast.service';
import { ConfirmService } from '../../../core/services/confirm.service';
import { PaginatedResult } from '../../../core/models/package.models';
import { GridPatternComponent } from '../../../shared/components/grid-pattern/grid-pattern';
import { PaginationComponent } from '../../../shared/components/pagination/pagination';

interface ScrapeLogInfo {
  id: number;
  packageId: string;
  version: string;
  registry: string;
  githubRepoUrl: string | null;
  failureReason: string;
  availableVersions: string | null;
  attemptedSources: string;
  isResolved: boolean;
  createdAt: string;
}

interface ScrapeLogPackageSummary {
  packageId: string;
  failureCount: number;
  latestReason: string | null;
  lastOccurrence: string;
}

interface ScrapeLogSummary {
  totalFailures: number;
  unresolvedCount: number;
  topFailingPackages: ScrapeLogPackageSummary[];
}

type ScrapeLogTab = 'summary' | 'logs';

@Component({
  selector: 'app-admin-scrape-logs',
  standalone: true,
  imports: [FormsModule, LucideAngularModule, GridPatternComponent, PaginationComponent],
  templateUrl: './admin-scrape-logs.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminScrapeLogsComponent {
  private api = inject(ApiService);
  private toast = inject(ToastService);
  private confirmService = inject(ConfirmService);

  readonly BugIcon = Bug;
  readonly TrashIcon = Trash2;
  readonly RefreshIcon = RefreshCw;
  readonly ExternalLinkIcon = ExternalLink;
  readonly PackageIcon = Package;
  readonly AlertIcon = AlertTriangle;

  activeTab = signal<ScrapeLogTab>('summary');

  summary = signal<ScrapeLogSummary | null>(null);
  logs = signal<PaginatedResult<ScrapeLogInfo> | null>(null);

  loading = signal(true);

  logsPage = signal(1);
  packageFilter = signal('');
  showResolved = signal(false);

  constructor() {
    this.loadData();
  }

  loadData(): void {
    if (this.activeTab() === 'summary') {
      this.loadSummary();
    } else {
      this.loadLogs();
    }
  }

  loadSummary(): void {
    this.loading.set(true);
    this.api.get<ScrapeLogSummary>('admin/scrape-logs/summary').subscribe({
      next: (data) => {
        this.summary.set(data);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.toast.error('Failed to load scrape log summary.');
      },
    });
  }

  loadLogs(): void {
    this.loading.set(true);
    const params: QueryParams = { page: this.logsPage(), pageSize: 20 };
    const packageFilter = this.packageFilter().trim();
    if (packageFilter) params['packageFilter'] = packageFilter;

    this.api.get<PaginatedResult<ScrapeLogInfo>>('admin/scrape-logs', params).subscribe({
      next: (data) => {
        this.logs.set(this.showResolved() ? data : { ...data, data: data.data.filter(log => !log.isResolved) });
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.toast.error('Failed to load scrape logs.');
      },
    });
  }

  switchTab(tab: ScrapeLogTab): void {
    this.activeTab.set(tab);
    this.loadData();
  }

  filterLogs(): void {
    this.goToPage(1);
  }

  clearFilter(): void {
    this.packageFilter.set('');
    this.goToPage(1);
  }

  toggleShowResolved(): void {
    this.showResolved.update(shown => !shown);
    this.goToPage(1);
  }

  goToPage(page: number): void {
    this.logsPage.set(page);
    this.loadLogs();
  }

  async resolveLog(log: ScrapeLogInfo): Promise<void> {
    if (!await this.confirmService.confirm(`Mark "${log.packageId} v${log.version}" as resolved?`)) return;

    this.api.put(`admin/scrape-logs/${log.id}/resolve`, {}).subscribe({
      next: () => {
        this.toast.success(`Scrape log for ${log.packageId} v${log.version} resolved.`);
        this.loadLogs();
      },
      error: () => this.toast.error('Failed to resolve scrape log.'),
    });
  }

  async resolveAllForPackage(packageId: string): Promise<void> {
    if (!await this.confirmService.confirm(`Resolve ALL scrape logs for "${packageId}"?`)) return;

    this.api.put(`admin/scrape-logs/resolve-package/${encodeURIComponent(packageId)}`, {}).subscribe({
      next: () => {
        this.toast.success(`All scrape logs for ${packageId} resolved.`);
        this.loadData();
      },
      error: () => this.toast.error('Failed to resolve scrape logs.'),
    });
  }

  async deleteLog(log: ScrapeLogInfo): Promise<void> {
    if (!await this.confirmService.confirm(`Delete scrape log for "${log.packageId} v${log.version}"?`)) return;

    this.api.delete(`admin/scrape-logs/${log.id}`).subscribe({
      next: () => {
        this.toast.success(`Scrape log deleted.`);
        this.loadLogs();
      },
      error: () => this.toast.error('Failed to delete scrape log.'),
    });
  }

  async clearResolved(): Promise<void> {
    if (!await this.confirmService.confirm('Clear all resolved scrape logs? This cannot be undone.')) return;

    this.api.delete('admin/scrape-logs/clear-resolved').subscribe({
      next: () => {
        this.toast.success('All resolved scrape logs cleared.');
        this.loadData();
      },
      error: () => this.toast.error('Failed to clear resolved logs.'),
    });
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

  getReasonColor(reason: string): string {
    if (reason.toLowerCase().includes('mismatch')) return 'text-orange-600 dark:text-orange-400';
    if (reason.toLowerCase().includes('no github')) return 'text-red-600 dark:text-red-400';
    return 'text-gray-600 dark:text-gray-400';
  }
}
