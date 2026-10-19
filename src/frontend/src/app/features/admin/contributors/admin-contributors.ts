import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { LucideAngularModule, RefreshCw, CheckCircle, AlertCircle, Trophy, Loader } from 'lucide-angular';
import { ApiService } from '../../../core/services/api';
import { ToastService } from '../../../core/services/toast.service';
import { PaginatedResult } from '../../../core/models/package.models';
import { GridPatternComponent } from '../../../shared/components/grid-pattern/grid-pattern';
import { PaginationComponent } from '../../../shared/components/pagination/pagination';

interface ContributorDto {
    gitHubUsername: string;
    gitHubUrl: string;
    mentionCount: number;
    rank: number;
}

type BackfillStatus = 'idle' | 'running' | 'done' | 'error';

const PAGE_SIZE = 20;
const RELOAD_AFTER_BACKFILL_MS = 3000;

@Component({
    selector: 'app-admin-contributors',
    standalone: true,
    imports: [LucideAngularModule, GridPatternComponent, PaginationComponent],
    templateUrl: './admin-contributors.html',
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminContributorsComponent {
    private api = inject(ApiService);
    private toast = inject(ToastService);
    private reloadTimer: ReturnType<typeof setTimeout> | null = null;

    readonly RefreshIcon = RefreshCw;
    readonly CheckIcon = CheckCircle;
    readonly AlertIcon = AlertCircle;
    readonly TrophyIcon = Trophy;
    readonly LoaderIcon = Loader;
    readonly skeletonRows = [1, 2, 3, 4, 5];

    result = signal<PaginatedResult<ContributorDto> | null>(null);
    loadingLeaderboard = signal(true);
    backfillStatus = signal<BackfillStatus>('idle');
    currentPage = signal(1);

    constructor() {
        this.loadLeaderboard();

        inject(DestroyRef).onDestroy(() => {
            if (this.reloadTimer) clearTimeout(this.reloadTimer);
        });
    }

    loadLeaderboard(): void {
        this.loadingLeaderboard.set(true);
        this.api.get<PaginatedResult<ContributorDto>>('contributors/paged', { page: this.currentPage(), pageSize: PAGE_SIZE }).subscribe({
            next: (data) => {
                this.result.set(data);
                this.loadingLeaderboard.set(false);
            },
            error: () => {
                this.loadingLeaderboard.set(false);
                this.toast.error('Failed to load contributors.');
            },
        });
    }

    goToPage(page: number): void {
        this.currentPage.set(page);
        this.loadLeaderboard();
    }

    triggerBackfill(): void {
        this.backfillStatus.set('running');
        this.api.post('contributors/backfill', {}).subscribe({
            next: () => {
                this.backfillStatus.set('done');
                this.toast.success('Backfill job enqueued. Reloading results in 3s...');
                if (this.reloadTimer) clearTimeout(this.reloadTimer);
                // The job runs in the background; its first results are there a moment later
                this.reloadTimer = setTimeout(() => {
                    this.reloadTimer = null;
                    this.backfillStatus.set('idle');
                    this.goToPage(1);
                }, RELOAD_AFTER_BACKFILL_MS);
            },
            error: () => {
                this.backfillStatus.set('error');
                this.toast.error('Failed to trigger backfill. Check Hangfire dashboard.');
            },
        });
    }

    getRankEmoji(rank: number): string {
        if (rank === 1) return '🥇';
        if (rank === 2) return '🥈';
        if (rank === 3) return '🥉';
        return `#${rank}`;
    }
}
