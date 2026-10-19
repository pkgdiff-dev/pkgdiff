import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';

import { LiveUpdatesService } from '../../../core/services/live-updates.service';
import { formatAge } from '../../../core/utils/live-updates.utils';

@Component({
    changeDetection: ChangeDetectionStrategy.OnPush,
    selector: 'app-live-ticker',
    standalone: true,
    imports: [],
    templateUrl: './live-ticker.html',
    styleUrl: './live-ticker.css',
})
export class LiveTickerComponent {
    private liveUpdates = inject(LiveUpdatesService);

    isLoading = this.liveUpdates.isLoading;
    tickerItems = computed(() =>
        this.liveUpdates.items().map((item) => ({ ...item, age: formatAge(item.updatedAt) }))
    );
}
