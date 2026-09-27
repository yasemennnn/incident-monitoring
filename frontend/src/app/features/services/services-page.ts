import { Component, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { describeError, isServiceUnavailable } from '../../core/http-errors';
import { ServiceStatus } from '../../core/models/api.models';
import { ApiService } from '../../core/services/api.service';
import { RealtimeService } from '../../core/services/realtime.service';
import { ServiceHealthTable } from '../../shared/components/service-health-table';

@Component({
  selector: 'app-services-page',
  imports: [ServiceHealthTable],
  template: `
    <div class="page-header"><h1>Service Status</h1></div>

    @if (error(); as message) {
      <p class="notice notice-warning" role="alert">{{ message }}</p>
    } @else if (services(); as list) {
      <section class="panel">
        <app-service-health-table [services]="list" />
        <p class="muted small">Open incidents = OPEN + ACKNOWLEDGED. Health is the worst severity among them.</p>
      </section>
    } @else {
      <p class="muted">Loading services…</p>
    }
  `,
})
export class ServicesPage {
  private readonly api = inject(ApiService);
  private readonly realtime = inject(RealtimeService);

  protected readonly services = signal<ServiceStatus[] | null>(null);
  protected readonly error = signal<string | null>(null);

  constructor() {
    this.load();
    // The summary already contains every service, so no extra request is needed.
    this.realtime.summaryUpdated$.pipe(takeUntilDestroyed()).subscribe((summary) => {
      this.services.set(summary.services);
      this.error.set(null);
    });
    this.realtime.reconnected$.pipe(takeUntilDestroyed()).subscribe(() => this.load());
  }

  private load(): void {
    this.api.getServices().subscribe({
      next: (services) => {
        this.services.set(services);
        this.error.set(null);
      },
      error: (error) => {
        this.services.set(null);
        this.error.set(
          isServiceUnavailable(error) ? 'Live service status is temporarily unavailable.' : describeError(error),
        );
      },
    });
  }
}
