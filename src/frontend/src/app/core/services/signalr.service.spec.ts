import { TestBed } from '@angular/core/testing';

import { HUB_CONNECTION_FACTORY, HubConnectionLike, SignalrService } from './signalr.service';

class FakeHubConnection implements HubConnectionLike {
  connectionId: string | null = 'connection-1';
  starts = 0;
  failStart = false;
  tokenRequests = 0;

  private handlers = new Map<string, (...args: unknown[]) => void>();
  private reconnecting: () => void = () => undefined;
  private reconnected: (connectionId?: string) => void = () => undefined;

  on<T extends unknown[]>(event: string, handler: (...args: T) => void): void {
    this.handlers.set(event, (...args: unknown[]) => handler(...(args as T)));
  }

  onreconnecting(callback: () => void): void {
    this.reconnecting = callback;
  }

  onreconnected(callback: (connectionId?: string) => void): void {
    this.reconnected = callback;
  }

  start(): Promise<void> {
    this.starts++;
    return this.failStart ? Promise.reject(new Error('offline')) : Promise.resolve();
  }

  invoke<T>(method: string): Promise<T> {
    expect(method).toBe('GetProgressToken');
    this.tokenRequests++;
    return Promise.resolve(`token-${this.tokenRequests}` as T);
  }

  emit(event: string, ...args: unknown[]): void {
    this.handlers.get(event)?.(...args);
  }

  dropAndReconnect(connectionId: string): void {
    this.reconnecting();
    this.connectionId = connectionId;
    this.reconnected(connectionId);
  }
}

describe('SignalrService', () => {
  let hub: FakeHubConnection;
  let created: number;
  let service: SignalrService;

  beforeEach(() => {
    hub = new FakeHubConnection();
    created = 0;
    TestBed.configureTestingModule({
      providers: [{
        provide: HUB_CONNECTION_FACTORY,
        useValue: async () => {
          created++;
          return hub;
        },
      }],
    });
    service = TestBed.inject(SignalrService);
  });

  it('opens one connection however many callers start it', async () => {
    await Promise.all([service.startConnection(), service.startConnection(), service.startConnection()]);

    expect(created).toBe(1);
    expect(hub.starts).toBe(1);
    expect(service.connectionId()).toBe('connection-1');
    expect(service.progressToken()).toBe('token-1');
  });

  it('reports the same package updated twice as two events', async () => {
    await service.startConnection();
    const seen: number[] = [];

    hub.emit('ReceivePackageUpdated', 'left-pad');
    const first = service.packageUpdated();
    seen.push(first!.sequence);
    hub.emit('ReceivePackageUpdated', 'left-pad');
    const second = service.packageUpdated();
    seen.push(second!.sequence);

    expect(first).not.toBe(second);
    expect(second).toEqual({ packageId: 'left-pad', sequence: seen[0] + 1 });
    expect(service.lastSequence).toBe(seen[1]);
  });

  it('numbers started and finished updates in one sequence, so a page can tell old events from new ones', async () => {
    await service.startConnection();

    hub.emit('ReceivePackageUpdateStarted', 'left-pad');
    hub.emit('ReceivePackageUpdated', 'left-pad');

    expect(service.packageUpdateStarted()!.sequence).toBeLessThan(service.packageUpdated()!.sequence);
  });

  it('drops the progress of the page that is being left', async () => {
    await service.startConnection();
    hub.emit('ReceiveProgress', 'Processing left-pad v1.0.0 (1/2)...');
    hub.emit('ReceiveBackgroundProgress', { packageId: 'left-pad', message: 'Updating (1/2)' });
    expect(service.progressMessage()).not.toBe('');

    service.resetProgress();

    expect(service.progressMessage()).toBe('');
    expect(service.backgroundProgress()).toBeNull();
  });

  it('takes a fresh connection id and progress token after a reconnect and counts it', async () => {
    await service.startConnection();

    hub.dropAndReconnect('connection-2');
    await Promise.resolve();

    expect(service.connectionId()).toBe('connection-2');
    expect(service.progressToken()).toBe('token-2');
    expect(service.reconnected()).toBe(1);
    expect(created).toBe(1);
  });

  it('keeps trying when the first start fails', async () => {
    vi.useFakeTimers();
    const logged = vi.spyOn(console, 'error').mockImplementation(() => undefined);
    try {
      hub.failStart = true;
      await expect(service.startConnection()).rejects.toThrow('offline');
      expect(service.connectionId()).toBeNull();

      hub.failStart = false;
      await vi.advanceTimersByTimeAsync(1000);

      expect(hub.starts).toBe(2);
      expect(service.connectionId()).toBe('connection-1');
      // The handlers are registered once, on the one connection that is reused
      expect(created).toBe(1);
    } finally {
      logged.mockRestore();
      vi.useRealTimers();
    }
  });
});
