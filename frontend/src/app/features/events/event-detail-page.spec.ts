import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { FakeRealtime, testEvent } from '../../../testing/fake-realtime';
import { EventDto } from '../../core/models/api.models';
import { ApiService } from '../../core/services/api.service';
import { RealtimeService } from '../../core/services/realtime.service';
import { EventDetailPage } from './event-detail-page';

async function render(event: EventDto, api: Record<string, unknown>) {
  TestBed.configureTestingModule({
    imports: [EventDetailPage],
    providers: [
      provideRouter([]),
      { provide: ApiService, useValue: { getEvent: vi.fn(() => of(event)), ...api } },
      { provide: RealtimeService, useValue: new FakeRealtime() },
    ],
  });
  const fixture = TestBed.createComponent(EventDetailPage);
  fixture.componentRef.setInput('id', event.eventId);
  await fixture.whenStable();
  const element = fixture.nativeElement as HTMLElement;
  const buttons = () => Array.from(element.querySelectorAll('.actions button')) as HTMLButtonElement[];
  return { fixture, element, buttons };
}

describe('EventDetailPage', () => {
  it('offers only the transitions allowed from OPEN and sends the chosen one', async () => {
    const updateStatus = vi.fn(() => of(testEvent({ status: 'RESOLVED' })));
    const { fixture, element, buttons } = await render(testEvent({ status: 'OPEN' }), { updateStatus });

    expect(buttons().map((b) => b.textContent?.trim())).toEqual(['Acknowledge', 'Resolve']);

    buttons()[1].click();
    await fixture.whenStable();

    expect(updateStatus).toHaveBeenCalledWith('EVT-1', 'RESOLVED');
    expect(element.textContent).toContain('Status is now RESOLVED.');
    expect(buttons().map((b) => b.textContent?.trim())).toEqual(['Reopen']);
  });

  it('shows the server reason on 409 and reloads the current state', async () => {
    const conflict = new HttpErrorResponse({
      status: 409,
      error: { title: 'Invalid status change', detail: 'Cannot change status from RESOLVED to ACKNOWLEDGED.' },
    });
    const getEvent = vi.fn()
      .mockReturnValueOnce(of(testEvent({ status: 'OPEN' })))
      .mockReturnValueOnce(of(testEvent({ status: 'RESOLVED' })));
    const { fixture, element, buttons } = await render(testEvent(), {
      getEvent,
      updateStatus: vi.fn(() => throwError(() => conflict)),
    });

    buttons()[0].click();
    await fixture.whenStable();

    expect(element.textContent).toContain('Cannot change status from RESOLVED to ACKNOWLEDGED.');
    expect(getEvent).toHaveBeenCalledTimes(2);
    expect(buttons().map((b) => b.textContent?.trim())).toEqual(['Reopen']);
  });
});
