import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { LucideAngularModule, Search, Package, Database, TrendingUp, Clock } from 'lucide-angular';
import { ApiService } from '../../../core/services/api';
import { ToastService } from '../../../core/services/toast.service';
import { GridPatternComponent } from '../../../shared/components/grid-pattern/grid-pattern';

interface DashboardStats {
  totalSearches: number;
  totalPackagesAnalyzed: number;
  cachedPackages: number;
  topPackages: { packageId: string; searchCount: number }[];
  recentSearches: { packageId: string; versionRange: string; searchedAt: string; processingTime: string }[];
  searchesByDay: Record<string, number>;
}

@Component({
  selector: 'app-admin-dashboard',
  standalone: true,
  imports: [LucideAngularModule, GridPatternComponent],
  templateUrl: './admin-dashboard.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminDashboardComponent {
  private api = inject(ApiService);
  private toast = inject(ToastService);

  readonly SearchIcon = Search;
  readonly PackageIcon = Package;
  readonly DatabaseIcon = Database;
  readonly TrendingIcon = TrendingUp;
  readonly ClockIcon = Clock;

  stats = signal<DashboardStats | null>(null);
  loading = signal(true);

  sortedDays = computed(() => {
    const data = this.stats()?.searchesByDay;
    if (!data) return [];
    return Object.entries(data)
      .map(([day, count]) => ({ day, count }))
      .sort((a, b) => a.day.localeCompare(b.day));
  });

  maxDayCount = computed(() => Math.max(...this.sortedDays().map(d => d.count), 1));

  constructor() {
    this.loadDashboard();
  }

  loadDashboard(): void {
    this.loading.set(true);
    this.api.get<DashboardStats>('admin/dashboard').subscribe({
      next: (data) => {
        this.stats.set(data);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.toast.error('Failed to load the dashboard.');
      },
    });
  }

  formatTime(time: string): string {
    const match = time.match(/(\d+):(\d+):(\d+)/);
    if (!match) return time;
    const [, h, m, s] = match;
    if (h !== '00') return `${h}h ${m}m`;
    if (m !== '00') return `${m}m ${s}s`;
    return `${s}s`;
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
}
