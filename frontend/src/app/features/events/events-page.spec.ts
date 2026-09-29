import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Subject, of } from 'rxjs';
import { FakeRealtime, testEvent } from '../../../testing/fake-realtime';
import { EventDto, EventFilters, PagedResult } from '../../core/models/api.models';
import { ApiService } from '../../core/services/api.service';
import { RealtimeService } from '../../core/services/realtime.service';
import { EventsPage } from './events-page';

describe('EventsPage', () => {
  it('goes back to page 1 and asks the backend again when a filter changes', async () => {
    const api = {
      getEvents: vi.fn((filters: EventFilters) =>
        of({ items: [testEvent()], page: filters.page, pageSize: 20, totalCount: 100 }),
      ),
      getFacets: vi.fn(() =>
        of({ sources: ['ATS'], services: ['route-service'], severities: ['INFO', 'CRITICAL'], statuses: ['OPEN'] }),
      ),
    };
    TestBed.configureTestingModule({
      imports: [EventsPage],
      providers: [
        provideRouter([]),
        { provide: ApiService, useValue: api },
        { provide: RealtimeService, useValue: new FakeRealtime() },
      ],
    });
    const fixture = TestBed.createComponent(EventsPage);
    await fixture.whenStable();
    const page = fixture.componentInstance;
    const element = fixture.nativeElement as HTMLElement;

    page.goToPage(3);
    expect(api.getEvents).toHaveBeenLastCalledWith(expect.objectContaining({ page: 3 }));

    const severity = element.querySelector<HTMLSelectElement>('select[formControlName="severity"]')!;
    severity.value = 'CRITICAL';
    severity.dispatchEvent(new Event('change'));
    await new Promise((resolve) => setTimeout(resolve, 350)); // filter changes are debounced (300 ms)

    expect(api.getEvents).toHaveBeenLastCalledWith(expect.objectContaining({ page: 1, severity: 'CRITICAL' }));
  });

  it('shows loading only while the first request runs, not after it failed', async () => {
    const response = new Subject<PagedResult<EventDto>>();
    TestBed.configureTestingModule({
      imports: [EventsPage],
      providers: [
        provideRouter([]),
        {
          provide: ApiService,
          useValue: {
            getEvents: vi.fn(() => response),
            getFacets: vi.fn(() => of({ sources: [], services: [], severities: [], statuses: [] })),
          },
        },
        { provide: RealtimeService, useValue: new FakeRealtime() },
      ],
    });
    const fixture = TestBed.createComponent(EventsPage);
    await fixture.whenStable();
    const text = () => (fixture.nativeElement as HTMLElement).textContent ?? '';

    expect(text()).toContain('Loading events…');

    response.error(new HttpErrorResponse({ status: 503, error: { title: 'Database temporarily unavailable' } }));
    await fixture.whenStable();

    expect(text()).toContain('Database temporarily unavailable');
    expect(text()).not.toContain('Loading events…');
    expect(text()).toContain('Events could not be loaded.');
  });

  describe('during a steady stream of SignalR messages', () => {
    async function render() {
      const api = {
        getEvents: vi.fn(() => of({ items: [testEvent()], page: 1, pageSize: 20, totalCount: 1 })),
        getFacets: vi.fn(() => of({ sources: [], services: [], severities: [], statuses: [] })),
      };
      const realtime = new FakeRealtime();
      TestBed.configureTestingModule({
        imports: [EventsPage],
        providers: [
          provideRouter([]),
          { provide: ApiService, useValue: api },
          { provide: RealtimeService, useValue: realtime },
        ],
      });
      const fixture = TestBed.createComponent(EventsPage);
      await fixture.whenStable();
      return { api, realtime };
    }

    afterEach(() => vi.useRealTimers());

    it('keeps reloading page 1 while new events arrive without a pause', async () => {
      const { api, realtime } = await render();
      vi.useFakeTimers();

      // One event every 100 ms for 3 s: the stream never pauses for the 1 s window.
      for (let i = 0; i < 30; i++) {
        realtime.eventReceived.next(testEvent({ eventId: `EVT-NEW-${i}` }));
        vi.advanceTimersByTime(100);
      }

      // The initial load plus about one reload per second while the events keep coming.
      expect(api.getEvents.mock.calls.length).toBeGreaterThanOrEqual(3);
    });

    it('keeps reloading while a visible event keeps being updated', async () => {
      const { api, realtime } = await render();
      vi.useFakeTimers();

      // EVT-1 is on the page; one update every 100 ms for 1.5 s never pauses for the 300 ms window.
      for (let i = 0; i < 15; i++) {
        realtime.eventUpdated.next(testEvent({ status: i % 2 ? 'OPEN' : 'ACKNOWLEDGED' }));
        vi.advanceTimersByTime(100);
      }

      expect(api.getEvents.mock.calls.length).toBeGreaterThanOrEqual(4);
    });
  });
});
