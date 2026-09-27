import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Observable, of, throwError } from 'rxjs';
import { FakeRealtime, testEvent } from '../../../testing/fake-realtime';
import { DashboardSummary } from '../../core/models/api.models';
import { ApiService } from '../../core/services/api.service';
import { RealtimeService } from '../../core/services/realtime.service';
import { DashboardPage } from './dashboard-page';

const summary: DashboardSummary = {
  totalEvents: 120,
  openEvents: 42,
  criticalEvents: 8,
  severityDistribution: { INFO: 70, WARNING: 30, MAJOR: 12, CRITICAL: 8 },
  statusDistribution: { OPEN: 42, ACKNOWLEDGED: 30, RESOLVED: 48 },
  services: [
    { name: 'route-service', status: 'DOWN', lastEventTime: '2026-06-01T10:15:00Z', latestSeverity: 'CRITICAL', openIncidentCount: 3 },
  ],
};

async function render(summaryResponse: () => Observable<DashboardSummary>) {
  const api = {
    getDashboardSummary: vi.fn(summaryResponse),
    getRecentEvents: vi.fn(() => of([testEvent()])),
  };
  const realtime = new FakeRealtime();
  TestBed.configureTestingModule({
    imports: [DashboardPage],
    providers: [
      provideRouter([]),
      { provide: ApiService, useValue: api },
      { provide: RealtimeService, useValue: realtime },
    ],
  });
  const fixture = TestBed.createComponent(DashboardPage);
  await fixture.whenStable();
  return { fixture, api, realtime, text: () => (fixture.nativeElement as HTMLElement).textContent ?? '' };
}

describe('DashboardPage', () => {
  it('shows the summary, service health and recent events from the API', async () => {
    const { text } = await render(() => of(summary));

    expect(text()).toContain('120');
    expect(text()).toContain('42');
    expect(text()).toContain('route-service');
    expect(text()).toContain('Route locking failed');
  });

  it('shows a calm message instead of numbers when dashboard data is unavailable (503)', async () => {
    const { text } = await render(() => throwError(() => new HttpErrorResponse({ status: 503 })));

    expect(text()).toContain('Live dashboard data is temporarily unavailable.');
    expect(text()).not.toContain('Total events');
  });

  it('replaces the summary when SignalR sends summaryUpdated', async () => {
    const { fixture, realtime, text } = await render(() => of(summary));

    realtime.summaryUpdated.next({ ...summary, totalEvents: 121 });
    await fixture.whenStable();

    expect(text()).toContain('121');
  });

  it('reloads everything over REST after a reconnect', async () => {
    const { fixture, api, realtime } = await render(() => of(summary));

    realtime.reconnected.next();
    await fixture.whenStable();

    expect(api.getDashboardSummary).toHaveBeenCalledTimes(2);
    expect(api.getRecentEvents).toHaveBeenCalledTimes(2);
  });
});
