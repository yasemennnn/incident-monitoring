import { DatePipe, LowerCasePipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { auditTime, merge } from 'rxjs';
import { describeError, isServiceUnavailable } from '../../core/http-errors';
import { DashboardSummary, EventDto, SEVERITIES, Severity } from '../../core/models/api.models';
import { ApiService } from '../../core/services/api.service';
import { RealtimeService } from '../../core/services/realtime.service';
import { ServiceHealthTable } from '../../shared/components/service-health-table';

@Component({
  selector: 'app-dashboard-page',
  imports: [DatePipe, LowerCasePipe, ServiceHealthTable],
  templateUrl: './dashboard-page.html',
})
export class DashboardPage {
  private readonly api = inject(ApiService);
  private readonly realtime = inject(RealtimeService);
  private readonly router = inject(Router);

  protected readonly summary = signal<DashboardSummary | null>(null);
  protected readonly summaryError = signal<string | null>(null);
  protected readonly recent = signal<EventDto[] | null>(null);
  protected readonly recentError = signal<string | null>(null);
  protected readonly severities = SEVERITIES;

  /** Largest severity count, used to scale the distribution bars. */
  private readonly maxSeverityCount = computed(() => {
    const distribution = this.summary()?.severityDistribution ?? {};
    return Math.max(1, ...SEVERITIES.map((s) => distribution[s] ?? 0));
  });

  constructor() {
    this.loadSummary();
    this.loadRecent();

    this.realtime.summaryUpdated$.pipe(takeUntilDestroyed()).subscribe((summary) => {
      this.summary.set(summary);
      this.summaryError.set(null);
    });
    // New or changed events can change the recent list; reload it at most twice a second,
    // also while events keep arriving without a pause.
    merge(this.realtime.eventReceived$, this.realtime.eventUpdated$)
      .pipe(auditTime(500), takeUntilDestroyed())
      .subscribe(() => this.loadRecent());
    this.realtime.reconnected$.pipe(takeUntilDestroyed()).subscribe(() => {
      this.loadSummary();
      this.loadRecent();
    });
  }

  protected severityCount(severity: Severity): number {
    return this.summary()?.severityDistribution[severity] ?? 0;
  }

  protected severityBarWidth(severity: Severity): number {
    return (this.severityCount(severity) / this.maxSeverityCount()) * 100;
  }

  protected openEvent(event: EventDto): void {
    void this.router.navigate(['/events', event.eventId]);
  }

  private loadSummary(): void {
    this.api.getDashboardSummary().subscribe({
      next: (summary) => {
        this.summary.set(summary);
        this.summaryError.set(null);
      },
      error: (error) => {
        // No old numbers are kept: showing stale counters as if they were current would be misleading.
        this.summary.set(null);
        this.summaryError.set(
          isServiceUnavailable(error) ? 'Live dashboard data is temporarily unavailable.' : describeError(error),
        );
      },
    });
  }

  private loadRecent(): void {
    this.api.getRecentEvents(10).subscribe({
      next: (events) => {
        this.recent.set(events);
        this.recentError.set(null);
      },
      error: (error) => {
        this.recentError.set(
          isServiceUnavailable(error) ? 'Recent events are temporarily unavailable.' : describeError(error),
        );
      },
    });
  }
}
