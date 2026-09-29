import { HttpErrorResponse } from '@angular/common/http';
import { DatePipe, LowerCasePipe } from '@angular/common';
import { Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { filter } from 'rxjs';
import { describeError } from '../../core/http-errors';
import { EventDto, EventStatus } from '../../core/models/api.models';
import { ALLOWED_TRANSITIONS, TRANSITION_LABELS } from '../../core/models/status-transitions';
import { ApiService } from '../../core/services/api.service';
import { RealtimeService } from '../../core/services/realtime.service';

@Component({
  selector: 'app-event-detail-page',
  imports: [DatePipe, LowerCasePipe, RouterLink],
  templateUrl: './event-detail-page.html',
})
export class EventDetailPage implements OnInit {
  private readonly api = inject(ApiService);
  private readonly realtime = inject(RealtimeService);

  /** From the route parameter :id. */
  readonly id = input.required<string>();

  protected readonly event = signal<EventDto | null>(null);
  protected readonly loadError = signal<string | null>(null);
  protected readonly actionError = signal<string | null>(null);
  protected readonly actionMessage = signal<string | null>(null);
  protected readonly saving = signal(false);
  protected readonly transitions = computed(() => {
    const event = this.event();
    return event ? ALLOWED_TRANSITIONS[event.status] : [];
  });
  protected readonly transitionLabels = TRANSITION_LABELS;

  constructor() {
    // Someone else may change this event while it is open; the message carries the full event.
    this.realtime.eventUpdated$
      .pipe(
        filter((event) => event.eventId === this.id()),
        takeUntilDestroyed(),
      )
      .subscribe((event) => this.event.set(event));
    this.realtime.reconnected$.pipe(takeUntilDestroyed()).subscribe(() => this.load());
  }

  ngOnInit(): void {
    this.load();
  }

  changeStatus(status: EventStatus): void {
    this.saving.set(true);
    this.actionError.set(null);
    this.actionMessage.set(null);

    // The status on screen: the buttons exist only while an event is shown.
    this.api.updateStatus(this.id(), status, this.event()!.status).subscribe({
      next: (updated) => {
        this.event.set(updated);
        this.actionMessage.set(`Status is now ${updated.status}.`);
        this.saving.set(false);
      },
      error: (error) => {
        this.actionError.set(describeError(error));
        this.saving.set(false);
        // 409: the status changed in the meantime. Show the server's reason and the current state.
        if (error instanceof HttpErrorResponse && (error.status === 409 || error.status === 404)) {
          this.load();
        }
      },
    });
  }

  private load(): void {
    this.api.getEvent(this.id()).subscribe({
      next: (event) => {
        this.event.set(event);
        this.loadError.set(null);
      },
      error: (error) => {
        this.event.set(null);
        this.loadError.set(describeError(error));
      },
    });
  }
}
