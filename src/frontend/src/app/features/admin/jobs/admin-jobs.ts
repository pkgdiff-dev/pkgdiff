import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { LucideAngularModule, Activity, Play, CheckCircle, XCircle, Clock, Loader2, RefreshCw, Zap, ChevronRight, Package, Trash2, ExternalLink } from 'lucide-angular';
import { environment } from '../../../../environments/environment';
import { ApiService } from '../../../core/services/api';
import { ToastService } from '../../../core/services/toast.service';
import { ConfirmService } from '../../../core/services/confirm.service';
import { GridPatternComponent } from '../../../shared/components/grid-pattern/grid-pattern';
import { HasPermissionDirective } from '../../../shared/directives/has-permission.directive';

interface RecurringJobInfo {
  id: string;
  cron: string;
  lastExecution: string | null;
  nextExecution: string | null;
  lastJobState: string;
}

interface RecentJobInfo {
  jobId: string;
  jobName: string;
  state: string;
  createdAt: string | null;
  completedAt: string | null;
  duration: number | null;
  exceptionMessage: string | null;
  packageId: string | null;
  childJobs: RecentJobInfo[];
}

interface QueuedJobInfo {
  jobId: string;
  jobName: string;
  state: string;
  enqueuedAt: string | null;
  startedAt: string | null;
  serverId: string | null;
  packageId: string | null;
}

interface JobsOverview {
  enqueued: number;
  processing: number;
  succeeded: number;
  failed: number;
  scheduled: number;
  recurringJobs: RecurringJobInfo[];
  recentJobs: RecentJobInfo[];
  queuedJobs: QueuedJobInfo[];
}

const REFRESH_INTERVAL_MS = 10000;

@Component({
  selector: 'app-admin-jobs',
  standalone: true,
  imports: [LucideAngularModule, GridPatternComponent, HasPermissionDirective],
  templateUrl: './admin-jobs.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminJobsComponent {
  private api = inject(ApiService);
  private toast = inject(ToastService);
  private confirmService = inject(ConfirmService);
  private followUps = new Set<ReturnType<typeof setTimeout>>();

  readonly ActivityIcon = Activity;
  readonly PlayIcon = Play;
  readonly CheckIcon = CheckCircle;
  readonly XIcon = XCircle;
  readonly ClockIcon = Clock;
  readonly LoaderIcon = Loader2;
  readonly RefreshIcon = RefreshCw;
  readonly ZapIcon = Zap;
  readonly ChevronIcon = ChevronRight;
  readonly PackageIcon = Package;
  readonly TrashIcon = Trash2;
  readonly ExternalLinkIcon = ExternalLink;

  data = signal<JobsOverview | null>(null);
  loading = signal(true);
  triggering = signal<string | null>(null);
  canceling = signal<string | null>(null);
  openingDashboard = signal(false);
  expandedJobs = signal<Set<string>>(new Set());

  constructor() {
    this.load();

    const autoRefresh = setInterval(() => this.silentLoad(), REFRESH_INTERVAL_MS);
    inject(DestroyRef).onDestroy(() => {
      clearInterval(autoRefresh);
      this.followUps.forEach(timer => clearTimeout(timer));
    });
  }

  load(): void {
    this.loading.set(true);
    this.api.get<JobsOverview>('admin/jobs').subscribe({
      next: (data) => {
        this.data.set(data);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.toast.error('Failed to load jobs.');
      },
    });
  }

  private silentLoad(): void {
    this.api.get<JobsOverview>('admin/jobs').subscribe({
      next: (data) => this.data.set(data),
      // A background refresh that fails keeps the last overview; the next one tries again
      error: () => undefined,
    });
  }

  // Hangfire needs a moment before a triggered or cancelled job shows up in the overview
  private silentLoadAfter(delayMs: number): void {
    const timer = setTimeout(() => {
      this.followUps.delete(timer);
      this.silentLoad();
    }, delayMs);
    this.followUps.add(timer);
  }

  triggerJob(jobId: string): void {
    this.triggering.set(jobId);
    this.api.post(`admin/jobs/${jobId}/trigger`, {}).subscribe({
      next: () => {
        this.triggering.set(null);
        this.toast.success(`Job "${jobId}" triggered.`);
        this.silentLoadAfter(1000);
      },
      error: () => {
        this.triggering.set(null);
        this.toast.error('Failed to trigger job.');
      },
    });
  }

  async cancelJob(jobId: string): Promise<void> {
    if (!await this.confirmService.confirm(`Cancel job #${jobId}?`)) return;

    this.canceling.set(jobId);
    this.api.delete(`admin/jobs/${jobId}`).subscribe({
      next: () => {
        this.canceling.set(null);
        this.toast.success(`Job #${jobId} cancelled.`);
        this.silentLoadAfter(500);
      },
      error: () => {
        this.canceling.set(null);
        this.toast.error('Failed to cancel job.');
      }
    });
  }

  openDashboard(): void {
    // Opened synchronously in the click handler so the popup blocker allows it
    const tab = window.open('about:blank', '_blank');
    this.openingDashboard.set(true);
    this.api.post<{ url: string }>('admin/jobs/dashboard-ticket', {}).subscribe({
      next: (res) => {
        // Empty in production, so the relative /hangfire path stays on the same origin.
        const origin = environment.apiUrl.replace(/\/api\/?$/, '');
        const url = origin + res.url;
        if (tab) {
          tab.opener = null;
          tab.location.href = url;
        } else {
          window.open(url, '_blank', 'noopener');
        }
        this.openingDashboard.set(false);
      },
      error: () => {
        tab?.close();
        this.openingDashboard.set(false);
        this.toast.error('Could not open the Hangfire dashboard.');
      },
    });
  }

  toggleExpand(jobId: string): void {
    this.expandedJobs.update(current => {
      const next = new Set(current);
      if (!next.delete(jobId)) next.add(jobId);
      return next;
    });
  }

  isExpanded(jobId: string): boolean {
    return this.expandedJobs().has(jobId);
  }

  formatDate(dateStr: string | null): string {
    if (!dateStr) return 'Never';
    const utcStr = dateStr.endsWith('Z') ? dateStr : dateStr + 'Z';
    return new Date(utcStr).toLocaleString('tr-TR', {
      day: '2-digit',
      month: '2-digit',
      year: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
    });
  }

  formatDuration(ms: number | null): string {
    if (ms == null) return '-';
    if (ms < 1000) return `${ms}ms`;
    const seconds = Math.floor(ms / 1000);
    if (seconds < 60) return `${seconds}s`;
    const minutes = Math.floor(seconds / 60);
    const remainingSeconds = seconds % 60;
    return `${minutes}m ${remainingSeconds}s`;
  }

  childSummary(job: RecentJobInfo): string {
    if (!job.childJobs?.length) return '';
    const succeeded = job.childJobs.filter(c => c.state === 'Succeeded').length;
    const failed = job.childJobs.filter(c => c.state === 'Failed').length;
    const parts: string[] = [];
    if (succeeded) parts.push(`${succeeded} succeeded`);
    if (failed) parts.push(`${failed} failed`);
    return `${job.childJobs.length} packages (${parts.join(', ')})`;
  }

  stateColor(state: string): string {
    switch (state.toLowerCase()) {
      case 'succeeded': return 'text-green-500';
      case 'failed': return 'text-red-500';
      case 'processing': return 'text-blue-500';
      case 'enqueued': return 'text-yellow-500';
      default: return 'text-gray-500';
    }
  }

  stateBg(state: string): string {
    switch (state.toLowerCase()) {
      case 'succeeded': return 'bg-green-50 dark:bg-green-900/20';
      case 'failed': return 'bg-red-50 dark:bg-red-900/20';
      case 'processing': return 'bg-blue-50 dark:bg-blue-900/20';
      case 'enqueued': return 'bg-yellow-50 dark:bg-yellow-900/20';
      default: return 'bg-gray-50 dark:bg-gray-800';
    }
  }
}
