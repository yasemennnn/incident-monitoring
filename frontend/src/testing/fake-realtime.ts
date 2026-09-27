import { signal } from '@angular/core';
import { Subject } from 'rxjs';
import { DashboardSummary, EventDto } from '../app/core/models/api.models';
import { ConnectionState } from '../app/core/services/realtime.service';

/** Stands in for RealtimeService in tests: the test pushes SignalR messages by hand. */
export class FakeRealtime {
  readonly eventReceived = new Subject<EventDto>();
  readonly eventUpdated = new Subject<EventDto>();
  readonly summaryUpdated = new Subject<DashboardSummary>();
  readonly reconnected = new Subject<void>();

  readonly eventReceived$ = this.eventReceived.asObservable();
  readonly eventUpdated$ = this.eventUpdated.asObservable();
  readonly summaryUpdated$ = this.summaryUpdated.asObservable();
  readonly reconnected$ = this.reconnected.asObservable();
  readonly state = signal<ConnectionState>('connected');

  start(): void {}
}

export function testEvent(overrides: Partial<EventDto> = {}): EventDto {
  return {
    eventId: 'EVT-1',
    source: 'ATS',
    service: 'route-service',
    severity: 'CRITICAL',
    message: 'Route locking failed',
    status: 'OPEN',
    timestamp: '2026-06-01T10:15:00.000Z',
    receivedAt: '2026-06-01T10:15:00.120Z',
    statusUpdatedAt: null,
    ...overrides,
  };
}
