import { Injectable, effect, inject, signal, untracked } from '@angular/core';
import { LiveUpdate } from '../models/package.models';
import { mergeLiveUpdate } from '../utils/live-updates.utils';
import { SignalrService } from './signalr.service';

// App-wide state of the Live Updates feed: the first page from the API, kept current by the hub
@Injectable({ providedIn: 'root' })
export class LiveUpdatesService {
  private signalr = inject(SignalrService);

  readonly limit = 20;
  items = signal<LiveUpdate[]>([]);
  isLoading = signal<boolean>(true);

  constructor() {
    effect(() => {
      const update = this.signalr.liveUpdate();
      if (update) {
        untracked(() => this.items.update((items) => mergeLiveUpdate(items, update, this.limit)));
      }
    });
  }

  set(items: LiveUpdate[]): void {
    this.items.set(items.slice(0, this.limit));
    this.isLoading.set(false);
  }

  loadFailed(): void {
    this.isLoading.set(false);
  }
}
