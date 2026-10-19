import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { LucideAngularModule, Activity, Database, Globe, RefreshCw, CheckCircle, XCircle, Server } from 'lucide-angular';
import { ApiService } from '../../../core/services/api';
import { apiErrorMessage } from '../../../core/utils/http-error.utils';
import { GridPatternComponent } from '../../../shared/components/grid-pattern/grid-pattern';

interface HealthDetails {
    overallStatus: string;
    database: DatabaseHealth;
    nuGet: ExternalServiceHealth;
    gitHub: ExternalServiceHealth;
    hangfire: HangfireHealth;
    checkedAt: string;
    securityProviders?: SecurityProviderHealth[];
}

interface SecurityProviderHealth {
    name: string;
    settingKey: string;
    enabled: boolean;
    isAvailable: boolean;
    responseTimeMs: number;
    errorMessage: string | null;
}

interface DatabaseHealth {
    isHealthy: boolean;
    responseTimeMs: number;
    errorMessage?: string;
}

interface ExternalServiceHealth {
    isAvailable: boolean;
    responseTimeMs: number;
    rateLimitRemaining?: number | null;
    rateLimitResetAt?: string | null;
    errorMessage?: string;
}

interface HangfireHealth {
    isHealthy: boolean;
    activeServers: number;
    failedJobs: number;
    enqueuedJobs: number;
    processingJobs: number;
    errorMessage?: string;
}

const REFRESH_INTERVAL_MS = 30000;

// Mirrors the backend buffer: at or below it, background scrapes pause until the reset
const GITHUB_RATE_LIMIT_BUFFER = 10;

@Component({
    selector: 'app-health-monitoring',
    standalone: true,
    imports: [LucideAngularModule, GridPatternComponent],
    templateUrl: './health-monitoring.component.html',
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class HealthMonitoringComponent {
    private api = inject(ApiService);

    readonly ActivityIcon = Activity;
    readonly DatabaseIcon = Database;
    readonly GlobeIcon = Globe;
    readonly RefreshIcon = RefreshCw;
    readonly CheckCircleIcon = CheckCircle;
    readonly XCircleIcon = XCircle;
    readonly ServerIcon = Server;

    healthDetails = signal<HealthDetails | null>(null);
    loading = signal(false);
    error = signal<string | null>(null);
    lastChecked = signal<Date | null>(null);

    constructor() {
        this.loadHealth();

        const autoRefresh = setInterval(() => this.loadHealth(), REFRESH_INTERVAL_MS);
        inject(DestroyRef).onDestroy(() => clearInterval(autoRefresh));
    }

    loadHealth(): void {
        this.loading.set(true);
        this.error.set(null);

        this.api.get<HealthDetails>('admin/health/details').subscribe({
            next: (details) => {
                this.healthDetails.set(details);
                this.lastChecked.set(new Date());
                this.loading.set(false);
            },
            error: (error: unknown) => {
                this.error.set(apiErrorMessage(error, 'Failed to load health status'));
                this.loading.set(false);
            },
        });
    }

    getStatusColor(status: string): string {
        switch (status.toLowerCase()) {
            case 'healthy': return 'text-green-500';
            case 'degraded': return 'text-yellow-500';
            case 'unhealthy': return 'text-red-500';
            default: return 'text-gray-500';
        }
    }

    getStatusBg(status: string): string {
        switch (status.toLowerCase()) {
            case 'healthy': return 'bg-green-50 dark:bg-green-900/20';
            case 'degraded': return 'bg-yellow-50 dark:bg-yellow-900/20';
            case 'unhealthy': return 'bg-red-50 dark:bg-red-900/20';
            default: return 'bg-gray-50 dark:bg-gray-900/20';
        }
    }

    getStatusIcon(isHealthy: boolean) {
        return isHealthy ? this.CheckCircleIcon : this.XCircleIcon;
    }

    isRateLimited(service: ExternalServiceHealth): boolean {
        return service.rateLimitRemaining != null && service.rateLimitRemaining <= GITHUB_RATE_LIMIT_BUFFER;
    }

    formatTime(dateString: string): string {
        return new Date(dateString).toLocaleTimeString();
    }
}
