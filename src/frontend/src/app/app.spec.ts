import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { App } from './app';
import { LiveUpdate, PaginatedResult } from './core/models/package.models';
import { LiveUpdatesService } from './core/services/live-updates.service';
import { PackageService } from './core/services/package.service';
import { SignalrService } from './core/services/signalr.service';

const update = (packageId: string, updatedAt: string): LiveUpdate => ({
  packageId,
  registry: 'npm',
  version: '1.0.0',
  previousVersion: null,
  updatedAt,
});

const pageOf = (data: LiveUpdate[]): PaginatedResult<LiveUpdate> => ({
  data,
  page: 1,
  pageSize: 20,
  total: data.length,
  totalPages: 1,
});

describe('App', () => {
  const signalr = {
    liveUpdate: signal<LiveUpdate | null>(null),
    reconnected: signal(0),
    startConnection: vi.fn(() => Promise.resolve()),
  };
  const packages = { getLiveUpdates: vi.fn() };

  beforeEach(async () => {
    signalr.liveUpdate.set(null);
    signalr.reconnected.set(0);
    signalr.startConnection.mockClear();
    packages.getLiveUpdates.mockReset();
    packages.getLiveUpdates.mockReturnValue(of(pageOf([update('a', '2026-10-01T10:00:00Z')])));

    await TestBed.configureTestingModule({
      imports: [App],
      providers: [
        { provide: SignalrService, useValue: signalr },
        { provide: PackageService, useValue: packages },
      ],
    }).compileComponents();
  });

  async function render() {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    await fixture.whenStable();
    return fixture;
  }

  it('should create the app', () => {
    const fixture = TestBed.createComponent(App);
    const app = fixture.componentInstance;
    expect(app).toBeTruthy();
  });

  it('should render router outlet', async () => {
    const fixture = await render();
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('router-outlet')).toBeTruthy();
  });

  it('loads the feed and opens the hub connection once, for every page', async () => {
    await render();
    const feed = TestBed.inject(LiveUpdatesService);

    expect(packages.getLiveUpdates).toHaveBeenCalledTimes(1);
    expect(packages.getLiveUpdates).toHaveBeenCalledWith(1, feed.limit);
    expect(signalr.startConnection).toHaveBeenCalledTimes(1);
    expect(feed.items().map((item) => item.packageId)).toEqual(['a']);
    expect(feed.isLoading()).toBe(false);
  });

  it('adds a pushed update to the feed without another request', async () => {
    await render();
    const feed = TestBed.inject(LiveUpdatesService);

    signalr.liveUpdate.set(update('b', '2026-10-02T10:00:00Z'));
    TestBed.tick();

    expect(feed.items().map((item) => item.packageId)).toEqual(['b', 'a']);
    expect(packages.getLiveUpdates).toHaveBeenCalledTimes(1);
  });

  it('reads the feed again after a reconnect', async () => {
    await render();
    packages.getLiveUpdates.mockReturnValue(
      of(pageOf([update('c', '2026-10-03T10:00:00Z'), update('a', '2026-10-01T10:00:00Z')])),
    );

    signalr.reconnected.set(1);
    TestBed.tick();

    expect(packages.getLiveUpdates).toHaveBeenCalledTimes(2);
    expect(TestBed.inject(LiveUpdatesService).items().map((item) => item.packageId)).toEqual(['c', 'a']);
  });

  it('stops the loading state when the feed cannot be read', async () => {
    packages.getLiveUpdates.mockReturnValue(throwError(() => new Error('offline')));

    await render();
    const feed = TestBed.inject(LiveUpdatesService);

    expect(feed.isLoading()).toBe(false);
    expect(feed.items()).toEqual([]);
  });
});
