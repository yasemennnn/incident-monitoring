# Frontend Integration Guide

Everything the Angular dashboard needs from the backend. All examples below are real responses from the running system.

| | |
|---|---|
| API base URL | `http://localhost:8080` |
| SignalR hub URL | `http://localhost:8080/hubs/incidents` |
| Swagger (try every endpoint) | `http://localhost:8080/swagger` |
| Allowed CORS origin | `http://localhost:4200` (with credentials) |

Start the backend with `docker compose up -d --build` from the repository root. A new event arrives every 2 seconds.

---

## Conventions

- JSON properties are **camelCase**.
- Enum values are **UPPERCASE strings**:

  | Field | Values |
  |---|---|
  | Severity | `INFO`, `WARNING`, `MAJOR`, `CRITICAL` |
  | Status | `OPEN`, `ACKNOWLEDGED`, `RESOLVED` |
  | Service status (health) | `HEALTHY`, `WARNING`, `DEGRADED`, `DOWN` |

- Dates are ISO-8601 strings in UTC, e.g. `"2026-09-25T11:42:38Z"`. They can be passed to `new Date(...)` or Angular's `date` pipe.
- Errors are ProblemDetails objects (see [Errors](#errors)).

### TypeScript shapes

```ts
export type Severity = 'INFO' | 'WARNING' | 'MAJOR' | 'CRITICAL';
export type EventStatus = 'OPEN' | 'ACKNOWLEDGED' | 'RESOLVED';
export type ServiceHealth = 'HEALTHY' | 'WARNING' | 'DEGRADED' | 'DOWN';

export interface IncidentEvent {
  eventId: string;
  source: string;
  service: string;
  severity: Severity;
  message: string;
  status: EventStatus;
  timestamp: string;              // when the event happened
  receivedAt: string;             // when the backend consumed it
  statusUpdatedAt: string | null; // last status change through the API
}

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
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
  severityDistribution: Record<Severity, number>;
  statusDistribution: Record<EventStatus, number>;
  services: ServiceStatus[];
  snapshotAt: string | null;      // time of the PostgreSQL snapshot the numbers come from
}

export interface EventFacets {
  sources: string[];
  services: string[];
  severities: Severity[];
  statuses: EventStatus[];
}
```

---

## Dashboard page

### `GET /api/dashboard/summary`

Provides the cards (total, open, critical), the data for the severity distribution chart, and the service health summary.

```json
{
  "totalEvents": 53,
  "openEvents": 35,
  "criticalEvents": 5,
  "severityDistribution": { "INFO": 29, "WARNING": 12, "MAJOR": 7, "CRITICAL": 5 },
  "statusDistribution": { "OPEN": 35, "ACKNOWLEDGED": 7, "RESOLVED": 11 },
  "services": [
    {
      "name": "passenger-info-service",
      "status": "DOWN",
      "lastEventTime": "2026-09-25T11:43:00Z",
      "latestSeverity": "INFO",
      "openIncidentCount": 12
    },
    {
      "name": "timetable-service",
      "status": "HEALTHY",
      "lastEventTime": "2026-09-25T11:42:44Z",
      "latestSeverity": "MAJOR",
      "openIncidentCount": 2
    }
  ],
  "snapshotAt": "2026-09-25T11:43:00.512345Z"
}
```

- `openEvents`: events with status `OPEN`.
- `criticalEvents`: `CRITICAL` events that are not `RESOLVED`. `severityDistribution.CRITICAL` counts all `CRITICAL` events, so it can be higher.
- `services` is sorted by name.
- `snapshotAt`: when the numbers were computed from PostgreSQL. It is `null` only before the first projection has been written.

The numbers come from the Redis dashboard projection. The endpoint returns `503` if Redis is unavailable.

### `GET /api/events/recent?count=10`

Provides the recent events list. It reads PostgreSQL directly and returns the latest events by `timestamp`, newest first, with `eventId` as tie-breaker. `count` is between 1 and 50 (default 10). It returns `503` if PostgreSQL is unavailable; it does not depend on Redis.

```json
[
  {
    "eventId": "EVT-85E7D4781C2B4F6A9D3E0B7C5A1F2E64",
    "source": "PIS",
    "service": "passenger-info-service",
    "severity": "INFO",
    "message": "Heartbeat received",
    "status": "RESOLVED",
    "timestamp": "2026-09-25T11:43:00Z",
    "receivedAt": "2026-09-25T11:43:00.485786Z",
    "statusUpdatedAt": null
  },
  {
    "eventId": "EVT-63A2327D8E0F4B1CA2D57E9F3B6C1D08",
    "source": "ATS",
    "service": "route-service",
    "severity": "INFO",
    "message": "Train arrived at platform",
    "status": "OPEN",
    "timestamp": "2026-09-25T11:42:58Z",
    "receivedAt": "2026-09-25T11:42:58.479597Z",
    "statusUpdatedAt": null
  }
]
```

---

## Events page

### `GET /api/events`

Returns the event table, sorted newest first. All parameters are optional and can be combined.

| Query parameter | Example | Meaning |
|---|---|---|
| `severity` | `CRITICAL` | Exact severity |
| `status` | `OPEN` | Exact status |
| `source` | `ATS` | Exact source |
| `service` | `route-service` | Exact service |
| `search` | `locking` | Case-insensitive text in event id, message or service |
| `page` | `1` | Page number, starts at 1 (default 1) |
| `pageSize` | `20` | 1-100 (default 20) |

```
GET /api/events?severity=CRITICAL&status=OPEN&source=ATS&search=route&page=1&pageSize=20
```

```json
{
  "items": [
    {
      "eventId": "EVT-61FF0B0D5A7C4E2D8F1B3C6E9A0D4F72",
      "source": "ATS",
      "service": "route-service",
      "severity": "MAJOR",
      "message": "Communication timeout with wayside unit",
      "status": "RESOLVED",
      "timestamp": "2026-09-25T11:42:48Z",
      "receivedAt": "2026-09-25T11:42:48.438504Z",
      "statusUpdatedAt": null
    }
  ],
  "page": 1,
  "pageSize": 2,
  "totalCount": 47
}
```

Number of pages: `Math.ceil(totalCount / pageSize)`.

An invalid parameter returns `400`:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "PageSize": ["The field PageSize must be between 1 and 100."],
    "Severity": ["The value 'FATAL' is not valid for Severity."]
  },
  "traceId": "00-c854433d0563c519d18b81c75c38e01c-b22592b6e1a758bf-00"
}
```

### `GET /api/events/facets`

Returns the values for the filter drop-downs. `sources` and `services` are the values seen so far; `severities` and `statuses` are all possible values.

```json
{
  "sources": ["ATS", "CBTC", "PIS", "SCADA"],
  "services": [
    "onboard-comm-service", "passenger-info-service", "power-supply-service", "route-service",
    "timetable-service", "train-tracking-service", "ventilation-service", "zone-controller-service"
  ],
  "severities": ["INFO", "WARNING", "MAJOR", "CRITICAL"],
  "statuses": ["OPEN", "ACKNOWLEDGED", "RESOLVED"]
}
```

### Event detail: `GET /api/events/{eventId}`

```json
{
  "eventId": "EVT-5C8A751E2D9B4A3FB6E1C0D8F7A5E239",
  "source": "PIS",
  "service": "passenger-info-service",
  "severity": "CRITICAL",
  "message": "Loss of communication with zone controller",
  "status": "OPEN",
  "timestamp": "2026-09-25T11:42:38Z",
  "receivedAt": "2026-09-25T11:42:38.397187Z",
  "statusUpdatedAt": null
}
```

Unknown id → `404`:

```json
{ "title": "Not found", "status": 404, "detail": "Event 'EVT-9F3C2A1B7E6D4C5B8A9F0E1D2C3B4A56' was not found.", "instance": "/api/events/EVT-9F3C2A1B7E6D4C5B8A9F0E1D2C3B4A56" }
```

### Status update: `PUT /api/events/{eventId}/status`

Request body:

```json
{ "status": "RESOLVED" }
```

`200` response, the updated event:

```json
{
  "eventId": "EVT-5C8A751E2D9B4A3FB6E1C0D8F7A5E239",
  "source": "PIS",
  "service": "passenger-info-service",
  "severity": "CRITICAL",
  "message": "Loss of communication with zone controller",
  "status": "RESOLVED",
  "timestamp": "2026-09-25T11:42:38Z",
  "receivedAt": "2026-09-25T11:42:38.397187Z",
  "statusUpdatedAt": "2026-09-25T11:43:08.2549714Z"
}
```

Allowed changes. Only show the buttons that are valid for the current status:

| Current status | Can change to |
|---|---|
| `OPEN` | `ACKNOWLEDGED`, `RESOLVED` |
| `ACKNOWLEDGED` | `RESOLVED` |
| `RESOLVED` | `OPEN` (reopen) |

Other responses:

| Status | When | Example |
|---|---|---|
| `409` | Change not allowed | `{ "title": "Invalid status change", "status": 409, "detail": "Cannot change status from RESOLVED to ACKNOWLEDGED.", "instance": "/api/events/EVT-5C8A751E2D9B4A3FB6E1C0D8F7A5E239/status" }` |
| `404` | Unknown event | same shape as above |
| `400` | Missing or unknown status value | `{ "title": "One or more validation errors occurred.", "status": 400, "errors": { "Status": ["The Status field is required."] } }` |

After a successful update, every connected client, including the one that made the change, also receives `eventUpdated` over SignalR. A `summaryUpdated` with the new numbers follows once the dashboard projection has been rebuilt.

---

## Service Status page

### `GET /api/services`

Returns the current status per service, the last event timestamp, the latest severity and the open incident count.

```json
[
  {
    "name": "onboard-comm-service",
    "status": "WARNING",
    "lastEventTime": "2026-09-25T11:41:24Z",
    "latestSeverity": "WARNING",
    "openIncidentCount": 1
  },
  {
    "name": "passenger-info-service",
    "status": "DOWN",
    "lastEventTime": "2026-09-25T11:43:00Z",
    "latestSeverity": "INFO",
    "openIncidentCount": 12
  },
  {
    "name": "train-tracking-service",
    "status": "DEGRADED",
    "lastEventTime": "2026-09-25T11:42:46Z",
    "latestSeverity": "WARNING",
    "openIncidentCount": 10
  }
]
```

- `status` is decided by the worst **open** incident, meaning one that is not RESOLVED: CRITICAL → `DOWN`, MAJOR → `DEGRADED`, WARNING → `WARNING`, otherwise `HEALTHY`.
- `openIncidentCount` counts `OPEN` and `ACKNOWLEDGED` events.
- `latestSeverity` is the severity of the service's most recent event. It can be `INFO` while the service is `DOWN`, because an older critical incident is still open.

The endpoint returns `503` if Redis is unavailable.

---

## SignalR

Install the client: `npm install @microsoft/signalr`

```ts
import * as signalR from '@microsoft/signalr';

const connection = new signalR.HubConnectionBuilder()
  .withUrl('http://localhost:8080/hubs/incidents')
  .withAutomaticReconnect()
  .build();

connection.on('eventReceived', (event: IncidentEvent) => { /* ... */ });
connection.on('eventUpdated', (event: IncidentEvent) => { /* ... */ });
connection.on('summaryUpdated', (summary: DashboardSummary) => { /* ... */ });

// Messages sent while disconnected are not replayed, so reload over REST after reconnecting.
connection.onreconnected(() => { /* reload summary, services, current events page */ });

await connection.start();
```

| Message | Payload | Sent when | Suggested handling |
|---|---|---|---|
| `eventReceived` | `IncidentEvent` | A new event was consumed from Kafka and stored in PostgreSQL | Prepend to the recent events list. On the events page, reload the current page or show a "new events" hint. |
| `eventUpdated` | `IncidentEvent` | An event's status was changed in PostgreSQL | Replace the event with the same `eventId` in any list or open detail |
| `summaryUpdated` | `DashboardSummary` | The dashboard projection was rebuilt and written to Redis | Replace the dashboard cards, chart data and service list |

`summaryUpdated` is not one-for-one with `eventReceived` / `eventUpdated`: several changes can be combined into one summary, it can arrive before or after the event message, and it is also sent by the periodic rebuild (every 30 seconds). It is not sent while Redis is unavailable.

Example `summaryUpdated` payload (same shape as `GET /api/dashboard/summary`):

```json
{
  "totalEvents": 56,
  "openEvents": 37,
  "criticalEvents": 6,
  "severityDistribution": { "INFO": 30, "WARNING": 12, "MAJOR": 8, "CRITICAL": 6 },
  "statusDistribution": { "OPEN": 37, "ACKNOWLEDGED": 8, "RESOLVED": 11 },
  "services": [],
  "snapshotAt": "2026-09-25T11:43:10.221304Z"
}
```

(`services` is abbreviated here. The real payload contains every service, as in the summary endpoint.)

The hub has no client-to-server methods and needs no authentication.

---

## Errors

Every error body is a ProblemDetails object:

```ts
export interface ProblemDetails {
  title: string;
  status: number;
  detail?: string;                    // human-readable reason (404 / 409)
  errors?: Record<string, string[]>;  // field errors (400)
}
```

| Status | Meaning |
|---|---|
| 400 | Invalid query parameter or body. See `errors`. |
| 404 | Event not found |
| 409 | Status change not allowed. See `detail`. |
| 503 | A backing store is temporarily unavailable: Redis for `/api/dashboard/summary` and `/api/services`, PostgreSQL for the event endpoints (including `/api/events/recent` and status changes). See `title`. |
| 500 | Unexpected server error |

---

## Adding the frontend to Docker Compose

`docker-compose.yml` contains a commented `frontend` service. To use it:
1. Add a Dockerfile to `frontend/` that builds the app and serves it on port 80, for example with nginx.
2. Uncomment the service. The app will then be available at `http://localhost:4200`, which is already the allowed CORS origin.
