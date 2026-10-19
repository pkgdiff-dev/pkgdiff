import { Injectable, InjectionToken, PLATFORM_ID, inject, signal } from '@angular/core';
import { isPlatformBrowser } from '@angular/common';
import { environment } from '../../../environments/environment';
import { LiveUpdate } from '../models/package.models';

const MAX_RETRY_DELAY_MS = 30000;
const retryDelay = (attempt: number) => Math.min(MAX_RETRY_DELAY_MS, 1000 * 2 ** Math.min(attempt, 5));

// The part of a SignalR connection this service uses
export interface HubConnectionLike {
    connectionId: string | null;
    on<T extends unknown[]>(event: string, handler: (...args: T) => void): void;
    onreconnecting(callback: () => void): void;
    onreconnected(callback: (connectionId?: string) => void): void;
    start(): Promise<void>;
    invoke<T>(method: string): Promise<T>;
}

// The SignalR client is loaded only when a connection is opened, which never happens on the server
export const HUB_CONNECTION_FACTORY = new InjectionToken<() => Promise<HubConnectionLike>>('HUB_CONNECTION_FACTORY', {
    providedIn: 'root',
    factory: () => async () => {
        const signalR = await import('@microsoft/signalr');
        return new signalR.HubConnectionBuilder()
            .withUrl(environment.hubUrl)
            // The default policy gives up after four attempts; this connection lives as long as the tab
            .withAutomaticReconnect({ nextRetryDelayInMilliseconds: context => retryDelay(context.previousRetryCount) })
            .build();
    },
});

// A new object for every event: the same package updated twice is two events, where its id alone
// would be the same signal value twice and notify nobody
export interface PackageEvent {
    packageId: string;
    sequence: number;
}

export interface BackgroundProgress {
    packageId: string;
    message: string;
}

@Injectable({
    providedIn: 'root'
})
export class SignalrService {
    private isBrowser = isPlatformBrowser(inject(PLATFORM_ID));
    private createConnection = inject(HUB_CONNECTION_FACTORY);
    private hubConnection: HubConnectionLike | null = null;
    private started: Promise<void> | null = null;
    private startAttempts = 0;
    private eventSequence = 0;

    progressMessage = signal<string>('');
    connectionId = signal<string | null>(null);
    progressToken = signal<string | null>(null);
    liveUpdate = signal<LiveUpdate | null>(null);
    // Counts reconnects: whatever was pushed while the connection was down has to be fetched again
    reconnected = signal<number>(0);
    packageUpdated = signal<PackageEvent | null>(null);
    packageUpdateStarted = signal<PackageEvent | null>(null);
    // New security data of a package was stored
    securityUpdated = signal<PackageEvent | null>(null);
    backgroundProgress = signal<BackgroundProgress | null>(null);

    // The number of the newest package event: a page that opens later can tell old events from new ones
    get lastSequence(): number {
        return this.eventSequence;
    }

    // One connection for the whole app: every caller gets the same start
    startConnection(): Promise<void> {
        if (!this.isBrowser) {
            return Promise.resolve();
        }
        if (!this.started) {
            this.started = this.start();
        }
        return this.started;
    }

    // Progress belongs to one package page; the next page starts without the last one's messages
    resetProgress(): void {
        this.progressMessage.set('');
        this.backgroundProgress.set(null);
    }

    private async connection(): Promise<HubConnectionLike> {
        if (this.hubConnection) {
            return this.hubConnection;
        }

        const connection = await this.createConnection();

        connection.on('ReceiveProgress', (message: string) => {
            this.progressMessage.set(message);
        });

        connection.on('ReceiveBackgroundProgress', (item: BackgroundProgress) => {
            this.backgroundProgress.set(item);
        });

        connection.on('ReceiveLiveUpdate', (update: LiveUpdate) => {
            this.liveUpdate.set(update);
        });

        connection.on('ReceivePackageUpdated', (packageId: string) => {
            this.packageUpdated.set({ packageId, sequence: ++this.eventSequence });
        });

        connection.on('ReceiveSecurityUpdated', (packageId: string) => {
            this.securityUpdated.set({ packageId, sequence: ++this.eventSequence });
        });

        connection.on('ReceivePackageUpdateStarted', (packageId: string) => {
            this.packageUpdateStarted.set({ packageId, sequence: ++this.eventSequence });
        });

        connection.onreconnecting(() => {
            this.connectionId.set(null);
            this.progressToken.set(null);
        });

        // A reconnect gets a new connection id, so the token bound to the old one is useless
        connection.onreconnected(connectionId => {
            this.connectionId.set(connectionId || null);
            this.progressToken.set(null);
            this.reconnected.update(count => count + 1);
            return this.refreshProgressToken();
        });

        this.hubConnection = connection;
        return connection;
    }

    private async start(): Promise<void> {
        try {
            const connection = await this.connection();
            await connection.start();
            this.startAttempts = 0;
            this.connectionId.set(connection.connectionId || null);
            await this.refreshProgressToken();
        } catch (err) {
            console.error('Error while starting SignalR connection: ' + err);
            // Automatic reconnect only covers a connection that was up once, so the first start retries here
            this.started = null;
            setTimeout(() => this.startConnection().catch(() => undefined), retryDelay(this.startAttempts++));
            throw err;
        }
    }

    // Without a token the analysis still runs; it just gets no progress messages
    private refreshProgressToken(): Promise<void> {
        const connection = this.hubConnection;
        if (!connection) {
            return Promise.resolve();
        }

        return connection
            .invoke<string>('GetProgressToken')
            .then(token => {
                if (this.hubConnection === connection) {
                    this.progressToken.set(token || null);
                }
            })
            .catch(err => {
                console.error('Error while fetching SignalR progress token: ' + err);
                this.progressToken.set(null);
            });
    }
}
