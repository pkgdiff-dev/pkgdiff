import { ChangeDetectionStrategy, Component, afterNextRender, effect, inject, untracked } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { ToastComponent } from './shared/components/toast/toast';
import { ConfirmModalComponent } from './shared/components/confirm-modal/confirm-modal.component';
import { RouteLoaderComponent } from './shared/components/route-loader/route-loader';
import { PackageService } from './core/services/package.service';
import { SignalrService } from './core/services/signalr.service';
import { LiveUpdatesService } from './core/services/live-updates.service';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet, ToastComponent, ConfirmModalComponent, RouteLoaderComponent],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App {
  private packageService = inject(PackageService);
  private signalr = inject(SignalrService);
  private liveUpdates = inject(LiveUpdatesService);

  constructor() {
    afterNextRender(() => {
      this.loadLiveUpdates();
      this.signalr.startConnection().catch(() => undefined);
    });

    // Updates pushed while disconnected are not replayed, so the feed is read again
    effect(() => {
      if (this.signalr.reconnected() > 0) {
        untracked(() => this.loadLiveUpdates());
      }
    });
  }

  private loadLiveUpdates(): void {
    this.packageService.getLiveUpdates(1, this.liveUpdates.limit).subscribe({
      next: (page) => this.liveUpdates.set(page.data),
      error: () => this.liveUpdates.loadFailed(),
    });
  }
}
