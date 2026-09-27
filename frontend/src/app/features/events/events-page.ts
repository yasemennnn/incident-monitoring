import { DatePipe, LowerCasePipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Subject, catchError, debounceTime, filter, of, switchMap, tap } from 'rxjs';
import { describeError } from '../../core/http-errors';
import { EventDto, EventFacets, PagedResult } from '../../core/models/api.models';
import { ApiService } from '../../core/services/api.service';
import { RealtimeService } from '../../core/services/realtime.service';

@Component({
  selector: 'app-events-page',
  imports: [DatePipe, LowerCasePipe, ReactiveFormsModule, RouterLink],
  templateUrl: './events-page.html',
})
export class EventsPage {
  private readonly api = inject(ApiService);
  private readonly realtime = inject(RealtimeService);

  protected readonly pageSize = 20;
  protected readonly filters = inject(NonNullableFormBuilder).group({
    severity: '',
    status: '',
    source: '',
    service: '',
    search: '',
  });
  protected readonly page = signal(1);
  protected readonly result = signal<PagedResult<EventDto> | null>(null);
  protected readonly facets = signal<EventFacets | null>(null);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly totalPages = computed(() =>
    Math.max(1, Math.ceil((this.result()?.totalCount ?? 0) / this.pageSize)),
  );

  private readonly reload$ = new Subject<void>();

  constructor() {
    // switchMap drops the previous request when a new one starts, so an older, slower
    // response can never overwrite the results of a newer filter.
    this.reload$
      .pipe(
        tap(() => this.loading.set(true)),
        switchMap(() =>
          this.api.getEvents({ ...this.filters.getRawValue(), page: this.page(), pageSize: this.pageSize }).pipe(
            catchError((error) => {
              this.error.set(describeError(error));
              return of(null);
            }),
          ),
        ),
        takeUntilDestroyed(),
      )
      .subscribe((result) => {
        this.loading.set(false);
        if (result) {
          this.result.set(result);
          this.error.set(null);
        }
      });

    this.filters.valueChanges.pipe(debounceTime(300), takeUntilDestroyed()).subscribe(() => this.onFiltersChanged());

    // A new event would appear on the first page (newest first). Rather than guessing whether it
    // matches the filters, ask the backend again.
    this.realtime.eventReceived$
      .pipe(
        filter(() => this.page() === 1),
        debounceTime(1000),
        takeUntilDestroyed(),
      )
      .subscribe(() => this.reload());
    this.realtime.eventUpdated$
      .pipe(
        filter((event) => this.isVisible(event.eventId)),
        debounceTime(300),
        takeUntilDestroyed(),
      )
      .subscribe(() => this.reload());
    this.realtime.reconnected$.pipe(takeUntilDestroyed()).subscribe(() => {
      this.loadFacets();
      this.reload();
    });

    this.loadFacets();
    this.reload();
  }

  onFiltersChanged(): void {
    this.page.set(1);
    this.reload();
  }

  goToPage(page: number): void {
    this.page.set(page);
    this.reload();
  }

  protected clearFilters(): void {
    this.filters.reset();
  }

  private reload(): void {
    this.reload$.next();
  }

  private isVisible(eventId: string): boolean {
    return this.result()?.items.some((e) => e.eventId === eventId) ?? false;
  }

  private loadFacets(): void {
    this.api.getFacets().subscribe({
      next: (facets) => this.facets.set(facets),
      error: (error) => this.error.set(describeError(error)),
    });
  }
}
