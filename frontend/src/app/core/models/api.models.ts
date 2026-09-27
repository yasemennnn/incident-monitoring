// Shapes of the backend REST API and SignalR messages (see docs/FRONTEND_INTEGRATION.md).

export type Severity = 'INFO' | 'WARNING' | 'MAJOR' | 'CRITICAL';
export type EventStatus = 'OPEN' | 'ACKNOWLEDGED' | 'RESOLVED';
export type ServiceHealth = 'HEALTHY' | 'WARNING' | 'DEGRADED' | 'DOWN';

/** Display order for charts, from least to most severe. */
export const SEVERITIES: Severity[] = ['INFO', 'WARNING', 'MAJOR', 'CRITICAL'];

export interface EventDto {
  eventId: string;
  source: string;
  service: string;
  severity: Severity;
  message: string;
  status: EventStatus;
  timestamp: string;
  receivedAt: string;
  statusUpdatedAt: string | null;
}

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
}

/** Query of GET /api/events. Empty strings mean "no filter". */
export interface EventFilters {
  severity?: string;
  status?: string;
  source?: string;
  service?: string;
  search?: string;
  page: number;
  pageSize: number;
}

export interface EventFacets {
  sources: string[];
  services: string[];
  severities: Severity[];
  statuses: EventStatus[];
}

export interface ServiceStatus {
  name: string;
  status: ServiceHealth;
  lastEventTime: string | null;
  latestSeverity: Severity | null;
  openIncidentCount: number;
}

export interface DashboardSummary {
  totalEvents: number;
  openEvents: number;
  criticalEvents: number;
  severityDistribution: Partial<Record<Severity, number>>;
  statusDistribution: Partial<Record<EventStatus, number>>;
  services: ServiceStatus[];
  /** Time of the projection snapshot. Not sent by the current backend yet, so it may be missing. */
  snapshotAt?: string;
}
