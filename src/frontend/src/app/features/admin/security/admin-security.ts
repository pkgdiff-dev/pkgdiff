import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { LucideAngularModule, Ban, RefreshCw } from 'lucide-angular';
import { ApiService, QueryParams } from '../../../core/services/api';
import { ToastService } from '../../../core/services/toast.service';
import { ConfirmService } from '../../../core/services/confirm.service';
import { PaginatedResult } from '../../../core/models/package.models';
import { GridPatternComponent } from '../../../shared/components/grid-pattern/grid-pattern';
import { ModalComponent } from '../../../shared/components/modal/modal';
import { PaginationComponent } from '../../../shared/components/pagination/pagination';

interface BlockedIpInfo {
  id: number;
  ipAddress: string;
  reason: string;
  blockType: string;
  expiresAt: string | null;
  createdAt: string;
  createdBy: string;
}

interface RequestLogInfo {
  id: number;
  ipAddress: string;
  endpoint: string;
  method: string;
  statusCode: number;
  createdAt: string;
  userAgent: string;
}

interface IpStatistics {
  ipAddress: string;
  requestCount: number;
  firstSeen: string;
  lastSeen: string;
  topEndpoints: string[];
  isBlocked: boolean;
}

interface BlockIpForm {
  ipAddress: string;
  reason: string;
  blockType: string;
  durationMinutes: number;
}

type SecurityTab = 'blocked' | 'logs' | 'stats';

const emptyBlockForm = (ipAddress = ''): BlockIpForm =>
  ({ ipAddress, reason: '', blockType: 'Temporary', durationMinutes: 60 });

@Component({
  selector: 'app-admin-security',
  standalone: true,
  imports: [FormsModule, LucideAngularModule, GridPatternComponent, ModalComponent, PaginationComponent],
  templateUrl: './admin-security.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminSecurityComponent {
  private api = inject(ApiService);
  private toast = inject(ToastService);
  private confirmService = inject(ConfirmService);

  readonly BanIcon = Ban;
  readonly RefreshIcon = RefreshCw;

  activeTab = signal<SecurityTab>('blocked');

  blockedIps = signal<PaginatedResult<BlockedIpInfo> | null>(null);
  requestLogs = signal<PaginatedResult<RequestLogInfo> | null>(null);
  ipStats = signal<IpStatistics[]>([]);

  loading = signal(true);
  showBlockModal = signal(false);
  blocking = signal(false);
  hideLocalhost = signal(true);

  blockedPage = signal(1);
  logsPage = signal(1);
  blockedIpFilter = signal('');
  logsIpFilter = signal('');

  formData = signal<BlockIpForm>(emptyBlockForm());

  constructor() {
    this.loadData();
  }

  loadData(): void {
    if (this.activeTab() === 'blocked') {
      this.loadBlockedIps();
    } else if (this.activeTab() === 'logs') {
      this.loadRequestLogs();
    } else {
      this.loadIpStats();
    }
  }

  loadBlockedIps(): void {
    this.loading.set(true);
    const params: QueryParams = { page: this.blockedPage(), pageSize: 20 };
    const ipFilter = this.blockedIpFilter().trim();
    if (ipFilter) params['ipFilter'] = ipFilter;

    this.api.get<PaginatedResult<BlockedIpInfo>>('admin/security/blocked-ips', params).subscribe({
      next: (data) => {
        this.blockedIps.set(this.withoutLocalhost(data));
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.toast.error('Failed to load blocked IPs.');
      },
    });
  }

  loadRequestLogs(): void {
    this.loading.set(true);
    const params: QueryParams = { page: this.logsPage(), pageSize: 50 };
    const ipFilter = this.logsIpFilter().trim();
    if (ipFilter) params['ipFilter'] = ipFilter;

    this.api.get<PaginatedResult<RequestLogInfo>>('admin/security/request-logs', params).subscribe({
      next: (data) => {
        this.requestLogs.set(this.withoutLocalhost(data));
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.toast.error('Failed to load request logs.');
      },
    });
  }

  loadIpStats(): void {
    this.loading.set(true);
    this.api.get<IpStatistics[]>('admin/security/ip-statistics', { topCount: 50 }).subscribe({
      next: (data) => {
        this.ipStats.set(this.hideLocalhost() ? data.filter(ip => !this.isLocalhost(ip.ipAddress)) : data);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.toast.error('Failed to load IP statistics.');
      },
    });
  }

  switchTab(tab: SecurityTab): void {
    this.activeTab.set(tab);
    this.loadData();
  }

  openBlockModal(ipAddress?: string): void {
    this.formData.set(emptyBlockForm(ipAddress || ''));
    this.showBlockModal.set(true);
  }

  closeBlockModal(): void {
    this.showBlockModal.set(false);
  }

  setField<K extends keyof BlockIpForm>(field: K, value: BlockIpForm[K]): void {
    this.formData.update(form => ({ ...form, [field]: value }));
  }

  blockIp(): void {
    if (this.blocking()) return;

    const form = this.formData();
    if (!form.ipAddress.trim() || !form.reason.trim()) {
      this.toast.warning('Please fill all required fields.');
      return;
    }

    this.blocking.set(true);
    this.api.post('admin/security/block-ip', form).subscribe({
      next: () => {
        this.blocking.set(false);
        this.toast.success(`IP ${form.ipAddress} blocked successfully.`);
        this.closeBlockModal();
        this.loadBlockedIps();
      },
      error: () => {
        this.blocking.set(false);
        this.toast.error('Failed to block IP.');
      },
    });
  }

  async quickBlockIp(ipAddress: string): Promise<void> {
    if (!await this.confirmService.confirm(`Block IP ${ipAddress}?`)) return;

    const blockData: BlockIpForm = {
      ipAddress,
      reason: 'Blocked from request logs',
      blockType: 'Temporary',
      durationMinutes: 60
    };

    this.api.post('admin/security/block-ip', blockData).subscribe({
      next: () => {
        this.toast.success(`IP ${ipAddress} blocked for 1 hour.`);
        this.loadRequestLogs();
        this.loadBlockedIps();
      },
      error: () => this.toast.error('Failed to block IP.'),
    });
  }

  async unblockIp(block: BlockedIpInfo): Promise<void> {
    if (!await this.confirmService.confirm(`Unblock IP ${block.ipAddress}?`)) return;
    this.api.delete(`admin/security/blocked-ips/${block.id}`).subscribe({
      next: () => {
        this.toast.success(`IP ${block.ipAddress} unblocked.`);
        this.loadBlockedIps();
      },
      error: () => this.toast.error('Failed to unblock IP.'),
    });
  }

  filterBlockedIps(): void {
    this.goToBlockedPage(1);
  }

  clearBlockedFilter(): void {
    this.blockedIpFilter.set('');
    this.goToBlockedPage(1);
  }

  filterLogs(): void {
    this.goToLogsPage(1);
  }

  clearLogsFilter(): void {
    this.logsIpFilter.set('');
    this.goToLogsPage(1);
  }

  toggleLocalhost(): void {
    this.hideLocalhost.update(hidden => !hidden);
    this.loadData();
  }

  goToBlockedPage(page: number): void {
    this.blockedPage.set(page);
    this.loadBlockedIps();
  }

  goToLogsPage(page: number): void {
    this.logsPage.set(page);
    this.loadRequestLogs();
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

  getStatusColor(code: number): string {
    if (code >= 200 && code < 300) return 'text-green-600 dark:text-green-400';
    if (code >= 400 && code < 500) return 'text-orange-600 dark:text-orange-400';
    if (code >= 500) return 'text-red-600 dark:text-red-400';
    return 'text-gray-600 dark:text-gray-400';
  }

  isLocalhost(ip: string): boolean {
    return ip === '::1' || ip === '127.0.0.1' || ip.startsWith('127.') || ip === 'localhost';
  }

  // Filtered client-side, so a page can come up short and its total counts the hidden rows
  private withoutLocalhost<T extends { ipAddress: string }>(page: PaginatedResult<T>): PaginatedResult<T> {
    if (!this.hideLocalhost()) return page;

    return { ...page, data: page.data.filter(row => !this.isLocalhost(row.ipAddress)) };
  }
}
