import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  DashboardSummary,
  EventDto,
  EventFacets,
  EventFilters,
  EventStatus,
  PagedResult,
  ServiceStatus,
} from '../models/api.models';

/** All calls to the backend REST API. */
@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/api`;

  getEvents(filters: EventFilters): Observable<PagedResult<EventDto>> {
    let params = new HttpParams().set('page', filters.page).set('pageSize', filters.pageSize);
    for (const key of ['severity', 'status', 'source', 'service', 'search'] as const) {
      const value = filters[key]?.trim();
      if (value) {
        params = params.set(key, value);
      }
    }
    return this.http.get<PagedResult<EventDto>>(`${this.baseUrl}/events`, { params });
  }

  getEvent(eventId: string): Observable<EventDto> {
    return this.http.get<EventDto>(`${this.baseUrl}/events/${encodeURIComponent(eventId)}`);
  }

  /** expectedStatus is the status the user saw: a change from any other status gets 409; asking for the current status still gets 200. */
  updateStatus(eventId: string, status: EventStatus, expectedStatus: EventStatus): Observable<EventDto> {
    return this.http.put<EventDto>(`${this.baseUrl}/events/${encodeURIComponent(eventId)}/status`, { status, expectedStatus });
  }

  getFacets(): Observable<EventFacets> {
    return this.http.get<EventFacets>(`${this.baseUrl}/events/facets`);
  }

  getRecentEvents(count = 10): Observable<EventDto[]> {
    return this.http.get<EventDto[]>(`${this.baseUrl}/events/recent`, { params: { count } });
  }

  getDashboardSummary(): Observable<DashboardSummary> {
    return this.http.get<DashboardSummary>(`${this.baseUrl}/dashboard/summary`);
  }

  getServices(): Observable<ServiceStatus[]> {
    return this.http.get<ServiceStatus[]>(`${this.baseUrl}/services`);
  }
}
