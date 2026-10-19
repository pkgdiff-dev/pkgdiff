import { ChangeDetectionStrategy, Component, DestroyRef, afterNextRender, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { LucideAngularModule, Github, Users, RefreshCw } from 'lucide-angular';
import { ApiService } from '../../core/services/api';
import { SeoService } from '../../core/services/seo.service';

interface ContributorDto {
    gitHubUsername: string;
    gitHubUrl: string;
    mentionCount: number;
    rank: number;
}

@Component({
    selector: 'app-contributors',
    standalone: true,
    imports: [CommonModule, RouterModule, LucideAngularModule],
    templateUrl: './contributors.html',
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ContributorsComponent {
    private api = inject(ApiService);
    private seo = inject(SeoService);
    private destroyRef = inject(DestroyRef);

    readonly GithubIcon = Github;
    readonly UsersIcon = Users;
    readonly RefreshIcon = RefreshCw;

    contributors = signal<ContributorDto[]>([]);
    loading = signal(true);
    countdown = signal(60);

    private refreshInterval: ReturnType<typeof setInterval> | null = null;
    private countdownInterval: ReturnType<typeof setInterval> | null = null;

    private readonly LIMIT = 100;
    private readonly REFRESH_SECS = 60;

    constructor() {
        this.seo.updateMeta({
            title: 'Meet the Contributors — pkgdiff',
            description: 'Meet the amazing open-source developers contributing to PkgDiff and helping make package upgrades seamless.',
            url: '/contributors',
            image: '/og-image/contributors.png'
        });
        this.loadData();

        // The list refreshes itself in the browser; a server render must not wait on a timer
        afterNextRender(() => this.startAutoRefresh());
        this.destroyRef.onDestroy(() => {
            this.clearTimers();
            this.seo.clearPageData();
        });
    }

    private loadData(): void {
        this.loading.set(true);
        this.api.get<ContributorDto[]>('contributors', { limit: this.LIMIT }).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
            next: (data) => {
                this.contributors.set(data);
                this.loading.set(false);
            },
            error: () => this.loading.set(false),
        });
    }

    private startAutoRefresh(): void {
        this.clearTimers();
        this.countdown.set(this.REFRESH_SECS);

        // Countdown ticker every second
        this.countdownInterval = setInterval(() => {
            this.countdown.update(v => v - 1);
        }, 1000);

        // Refresh + reset countdown every 60s
        this.refreshInterval = setInterval(() => {
            this.countdown.set(this.REFRESH_SECS);
            this.loadData();
        }, this.REFRESH_SECS * 1000);
    }

    manualRefresh(): void {
        this.clearTimers();
        this.loadData();
        this.startAutoRefresh();
    }

    private clearTimers(): void {
        if (this.refreshInterval) { clearInterval(this.refreshInterval); this.refreshInterval = null; }
        if (this.countdownInterval) { clearInterval(this.countdownInterval); this.countdownInterval = null; }
    }

    getRankClass(rank: number): string {
        if (rank === 1) return 'text-yellow-400';
        if (rank === 2) return 'text-slate-300';
        if (rank === 3) return 'text-amber-600';
        return 'text-slate-500 dark:text-slate-400';
    }

    getRankIcon(rank: number): string {
        if (rank === 1) return '🥇';
        if (rank === 2) return '🥈';
        if (rank === 3) return '🥉';
        return `#${rank}`;
    }
}
