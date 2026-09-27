import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { FakeRealtime, testEvent } from '../../../testing/fake-realtime';
import { EventFilters } from '../../core/models/api.models';
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
});
