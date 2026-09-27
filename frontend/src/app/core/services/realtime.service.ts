import { Injectable, signal } from '@angular/core';
import { HubConnection, HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { Subject } from 'rxjs';
import { environment } from '../../../environments/environment';
import { DashboardSummary, EventDto } from '../models/api.models';

export type ConnectionState = 'connecting' | 'connected' | 'reconnecting' | 'offline';

/**
 * The SignalR connection to /hubs/incidents.
 *
 * REST gives the pages their data; SignalR only tells them that something changed.
 * Messages sent while we are disconnected are not replayed, so after every reconnect
 * `reconnected$` fires and the pages reload their data over REST.
 */
@Injectable({ providedIn: 'root' })
export class RealtimeService {
  private readonly received = new Subject<EventDto>();
  private readonly updated = new Subject<EventDto>();
  private readonly summary = new Subject<DashboardSummary>();
  private readonly reconnectedSubject = new Subject<void>();

  readonly eventReceived$ = this.received.asObservable();
  readonly eventUpdated$ = this.updated.asObservable();
  readonly summaryUpdated$ = this.summary.asObservable();
  readonly reconnected$ = this.reconnectedSubject.asObservable();
  readonly state = signal<ConnectionState>('offline');

  private readonly connection: HubConnection;
  private started = false;
  private firstAttempt = true;

  constructor() {
    this.connection = new HubConnectionBuilder()
      .withUrl(environment.hubUrl)
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();

    this.connection.on('eventReceived', (event: EventDto) => this.received.next(event));
    this.connection.on('eventUpdated', (event: EventDto) => this.updated.next(event));
    this.connection.on('summaryUpdated', (summary: DashboardSummary) => this.summary.next(summary));

    this.connection.onreconnecting(() => this.state.set('reconnecting'));
    this.connection.onreconnected(() => {
      this.state.set('connected');
      this.reconnectedSubject.next();
    });
    // Automatic reconnect gives up after a few attempts (0, 2, 10, 30 s). After that we keep trying.
    this.connection.onclose(() => {
      this.state.set('offline');
      this.retryLater();
    });
  }

  /** Called once by the app shell. */
  start(): void {
    if (this.started) {
      return;
    }
    this.started = true;
    void this.connect();
  }

  private async connect(): Promise<void> {
    this.state.set('connecting');
    try {
      await this.connection.start();
      this.state.set('connected');
      // Pages load their data themselves at startup; only a later connection means data may be stale.
      if (!this.firstAttempt) {
        this.reconnectedSubject.next();
      }
    } catch {
      this.state.set('offline');
      this.retryLater();
    } finally {
      this.firstAttempt = false;
    }
  }

  private retryLater(): void {
    setTimeout(() => void this.connect(), 5000);
  }
}
