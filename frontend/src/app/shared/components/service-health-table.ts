import { DatePipe, LowerCasePipe } from '@angular/common';
import { Component, input } from '@angular/core';
import { ServiceStatus } from '../../core/models/api.models';

/** Health of every service. Used on the dashboard and on the Service Status page. */
@Component({
  selector: 'app-service-health-table',
  imports: [DatePipe, LowerCasePipe],
  template: `
    <div class="table-wrap">
      <table>
        <caption class="visually-hidden">Service health</caption>
        <thead>
          <tr>
            <th scope="col">Service</th>
            <th scope="col">Health</th>
            <th scope="col">Latest severity</th>
            <th scope="col" class="num" title="OPEN + ACKNOWLEDGED">Open incidents</th>
            <th scope="col">Last event</th>
          </tr>
        </thead>
        <tbody>
          @for (service of services(); track service.name) {
            <tr>
              <td>{{ service.name }}</td>
              <td><span [class]="'badge health-' + (service.status | lowercase)">{{ service.status }}</span></td>
              <td>
                @if (service.latestSeverity) {
                  <span [class]="'badge sev-' + (service.latestSeverity | lowercase)">{{ service.latestSeverity }}</span>
                } @else {
                  <span class="muted">–</span>
                }
              </td>
              <td class="num">{{ service.openIncidentCount }}</td>
              <td>{{ service.lastEventTime ? (service.lastEventTime | date: 'yyyy-MM-dd HH:mm:ss') : '–' }}</td>
            </tr>
          } @empty {
            <tr><td colspan="5" class="empty">No services have reported events yet.</td></tr>
          }
        </tbody>
      </table>
    </div>
  `,
})
export class ServiceHealthTable {
  readonly services = input.required<ServiceStatus[]>();
}
