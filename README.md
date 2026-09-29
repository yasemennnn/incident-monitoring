# Real-Time Incident Monitoring Dashboard

A small real-time monitoring system:
- A **producer** publishes operational events to a 3-node **Kafka** cluster.
- A **.NET 8** backend consumes and validates them, stores them in **PostgreSQL** and keeps a dashboard projection in **Redis**. It exposes a **REST API** and pushes changes over **SignalR**.
- An **Angular** dashboard ([`frontend/`](frontend/README.md)), served by nginx on http://localhost:4200, shows the data.

Everything runs with Docker Compose. [docs/FRONTEND_INTEGRATION.md](docs/FRONTEND_INTEGRATION.md) describes the API contract the dashboard uses.

---

## 1. Architecture

```mermaid
flowchart LR
    P[Producer<br/>.NET worker] -->|JSON event<br/>key = service| K[(Kafka, 3 nodes<br/>incident-events<br/>RF 3, min ISR 2)]
    K -->|consumer group| C[Kafka consumer]
    C -->|invalid / conflict| DLQ[(Kafka<br/>incident-events-dlq)]

    subgraph API [IncidentMonitoring.Api]
        C --> EP[EventProcessor]
        R[REST controllers] --> ES[EventService]
        R --> DS[DashboardService]
        W[RedisProjectionWorker]
        H[SignalR hub<br/>/hubs/incidents]
    end

    EP -->|insert| PG[(PostgreSQL<br/>source of truth)]
    ES -->|read / conditional update| PG
    EP -.->|refresh request| W
    ES -.->|refresh request| W
    W -->|snapshot| PG
    W -->|rewrite| RD[(Redis<br/>dashboard projection)]
    DS -->|read| RD
    EP -->|eventReceived| H
    ES -->|eventUpdated| H
    W -->|summaryUpdated| H
    R <-->|HTTP| UI[Angular dashboard]
    H -->|WebSocket push| UI
```

### What happens to one event

```
Kafka message
  → deserialize and validate      (invalid → dead-letter topic)
  → insert into PostgreSQL        (same eventId and same content → duplicate, ignored;
                                   same eventId, different content → conflict → dead-letter topic)
  → request a dashboard refresh   (only sets a flag in memory; Redis is not called here)
  → push eventReceived over SignalR
  → commit the Kafka offset
```

Separately, the projection worker rebuilds the Redis dashboard from PostgreSQL and then pushes `summaryUpdated`.

This flow is implemented in [`EventProcessor.ProcessAsync`](backend/IncidentMonitoring.Core/Services/EventProcessor.cs), which [`KafkaConsumerService`](backend/IncidentMonitoring.Infrastructure/Kafka/KafkaConsumerService.cs) calls for each message.

### Why three data technologies?

| Technology | Role |
|---|---|
| **Kafka** | Event ingestion between the producer and the backend, with a dead-letter topic ([§4](#4-kafka-design)) |
| **PostgreSQL** | Source of truth: every stored event and every status change ([§6](#6-postgresql)) |
| **Redis** | Precomputed dashboard numbers and service state, so the dashboard does not aggregate the events table on every request ([§5](#5-redis-dashboard-projection)) |

### Projects

| Project | Contents |
|---|---|
| `IncidentMonitoring.Api` | Controllers, SignalR hub and notifier, global exception handler, `Program.cs` (startup and DI) |
| `IncidentMonitoring.Core` | Models, enums, DTOs, interfaces, business services (`EventProcessor`, `EventService`, `DashboardService`), rules (validation, duplicate comparison, status changes, service health, dashboard projection) |
| `IncidentMonitoring.Infrastructure` | EF Core `AppDbContext` and `EventRepository`, `RedisDashboardStore`, `RedisProjectionWorker`, `KafkaConsumerService`, health checks |
| `IncidentMonitoring.Producer` | Standalone worker that publishes random events |
| `IncidentMonitoring.Tests` | Unit tests with in-memory fakes (no Docker needed) |
| `IncidentMonitoring.IntegrationTests` | `EventRepository` tests against a real PostgreSQL started with Testcontainers |
| `frontend/` | Angular dashboard, built and served by nginx in Docker |

Core defines four interfaces (`IEventRepository`, `IDashboardStore`, `IDashboardRefresher`, `IEventNotifier`) and has no infrastructure dependency, so its logic is unit-tested with fakes.

---

## 2. Technology choices

| Area | Choice |
|---|---|
| Backend | .NET 8 Web API |
| Messaging | Apache Kafka 3.9, 3 nodes in KRaft mode (no ZooKeeper), Confluent.Kafka client |
| Persistence | PostgreSQL 16 with EF Core 8 (Npgsql) and migrations |
| Dashboard projection | Redis 7 with StackExchange.Redis |
| Real-time | ASP.NET Core SignalR |
| Frontend | Angular 21, production build served by nginx in Docker ([frontend/README.md](frontend/README.md)) |
| API docs | Swagger / OpenAPI (Swashbuckle) |
| Logging | Built-in .NET logging, JSON console output in Docker |
| Tests | xUnit, Testcontainers (PostgreSQL) for integration tests, Vitest for Angular |
| Runtime | Docker Compose |

---

## 3. How to run

Requirements: Docker Desktop. These host ports must be free: 4200, 8080, 8081, 5432, 6379, 29092-29094.

```bash
docker compose up -d --build
```

The first build takes a few minutes (images, `dotnet restore`, `npm ci`, Angular build).

| URL | What |
|---|---|
| http://localhost:4200 | Dashboard: Dashboard, Events, Service Status |
| http://localhost:8080/swagger | API documentation, where every endpoint can be tried |
| http://localhost:8080/health | Health check (PostgreSQL, Redis, Kafka) |
| http://localhost:8081 | Kafka UI: topics, messages, consumer group, dead-letter topic |

Open the dashboard as `localhost`, not `127.0.0.1`: the API allows only the origin `http://localhost:4200`. The API starts after the Kafka cluster is ready. If the dashboard is opened before that, it shows "Offline, retrying" and reconnects on its own.

**Sample data:** there is no seed step. The producer starts with the stack and publishes a random event every 2 seconds. For a controlled run, stop it and send a fixed number of events (commands below).

Docker services:

| Service | Purpose |
|---|---|
| `kafka-1`, `kafka-2`, `kafka-3` | Kafka nodes, each both broker and controller (KRaft) |
| `kafka-init` | Waits until all three brokers are registered, creates the two topics, then exits |
| `kafka-ui` | Web UI for Kafka |
| `redis` | Dashboard projection (append-only file enabled) |
| `postgres` | Event storage |
| `api` | REST API, SignalR hub, Kafka consumer and Redis projection worker |
| `producer` | Publishes one random event every 2 seconds |
| `frontend` | Angular production build served by nginx on port 4200 |

The API creates the database schema on startup (EF Core migrations).

Useful commands:

```bash
docker compose logs -f api                 # consumer and API logs (JSON)
docker compose stop producer               # pause the event stream
docker compose run --rm -e Producer__Count=20 -e Producer__IntervalMs=100 producer   # manual run: send 20 events and exit
docker compose down -v                     # stop everything and delete all data
```

### Running the backend without Docker

```bash
docker compose up -d kafka-1 kafka-2 kafka-3 kafka-init redis postgres
cd backend
dotnet run --project IncidentMonitoring.Api         # http://localhost:8080
dotnet run --project IncidentMonitoring.Producer    # in a second terminal
```

`appsettings.json` points to `localhost` (Kafka on `localhost:29092,localhost:29093,localhost:29094`). In Docker the same settings are overridden with environment variables (`Kafka__BootstrapServers=kafka-1:9092,kafka-2:9092,kafka-3:9092`, and so on).

For Angular development, run `docker compose stop frontend` first (the container and `npm start` both use port 4200), then follow [frontend/README.md](frontend/README.md).

### Configuration

Settings a reviewer is most likely to change. Set them as environment variables on the service in `docker-compose.yml`, or with `-e` on `docker compose run`.

| Variable | Service | Default | Meaning |
|---|---|---|---|
| `Producer__IntervalMs` | producer | `2000` | Delay between two events |
| `Producer__Count` | producer | `0` | Number of events to send before exiting; `0` means run until stopped |
| `Kafka__MaxAttempts` | api | `3` | Processing attempts before a message goes to the dead-letter topic (not used for PostgreSQL outages) |
| `DashboardProjection__RefreshIntervalSeconds` | api | `30` | Periodic rebuild of the Redis projection |
| `EventValidation__MaxFutureSkew` | api | `00:05:00` | How far an event timestamp may be in the future |
| `Cors__AllowedOrigin` | api | `http://localhost:4200` | Origin allowed to call the API and the SignalR hub |

Connection strings and Kafka bootstrap servers are already set for the Compose network.

---

## 4. Kafka design

### Cluster

Three nodes, `kafka-1` to `kafka-3`, each acting as both broker and controller (combined KRaft mode), with one fixed cluster id. Every node keeps its data in its own named volume.

| Setting | Value | Why |
|---|---|---|
| Replication factor | 3 | Every partition has a copy on every node |
| `min.insync.replicas` | 2 | A write with `acks=all` is confirmed only once 2 nodes have it, so one node can fail without losing confirmed events. With two nodes down, writes are refused instead of being accepted unsafely. |
| Consumer offsets topic | replication factor 3 | Committed offsets survive the loss of one node |
| Unclean leader election | disabled | A replica that is behind never becomes leader and silently drops confirmed messages |

Containers use `kafka-N:9092`; programs on the host use `localhost:29092`, `29093` and `29094`.

### Topics

| Topic | Partitions | Replication | Purpose |
|---|---|---|---|
| `incident-events` | 3 | RF 3, min ISR 2 | Incoming events |
| `incident-events-dlq` | 1 | RF 3, min ISR 2 | Messages that could not be processed |

The topics are created by the `kafka-init` container. The dead-letter topic is as durable as the main topic because the main offset is committed only after the dead-letter write is confirmed.

**Producer**
- The message **key is the service name**, so all events of one service go to the same partition and are consumed in order.
- `acks=all` and idempotence are enabled.
- Event ids are `EVT-` followed by a random UUID (32 uppercase hex digits). Timestamps are UTC with milliseconds.
- The interval is configurable (`Producer__IntervalMs`). `Producer__Count` sends a fixed number of events and exits, for manual runs. On stop it logs how many events were attempted and how many were published.

**Consumer** (`KafkaConsumerService`)
- **Consumer group** `incident-monitoring-api`, consuming the 3 partitions of `incident-events`.
- **Manual offset commit** (`EnableAutoCommit=false`). The offset is committed only after a message has a final outcome: stored, ignored as a duplicate, or confirmed in the dead-letter topic. If the API stops in the middle, the message is delivered again. Duplicates are then recognised in PostgreSQL, so nothing is lost or counted twice.
- A new consumer group starts from the beginning of the topic (`AutoOffsetReset=Earliest`).

### Retry and dead-letter topic

| Situation | Behaviour |
|---|---|
| Invalid message (bad JSON, missing field, unknown severity or status, timestamp without a time zone or more than 5 minutes in the future, unsafe characters in `eventId`) | Sent to `incident-events-dlq` immediately. Retrying cannot fix it. |
| Same `eventId` and same content (source, service, severity, message, timestamp) | Duplicate: logged and ignored. A different status in the message does not overwrite the stored status. |
| Same `eventId`, different content | Conflict: the stored event is kept and the message is sent to `incident-events-dlq`. |
| PostgreSQL temporarily unavailable | Nothing is committed and nothing goes to the dead-letter topic. The consumer seeks back to the same offset and reads it again after 1 s, 2 s, 4 s … up to 30 s, for as long as the outage lasts. |
| Any other processing error | Retried up to `MaxAttempts` = 3 times with waits of 1 s and 2 s, then sent to `incident-events-dlq`. |
| Kafka offset commit fails | Logged. The message may be delivered again, which is then handled as a duplicate. |

A dead-lettered message keeps its original key and value. These headers are added:
- `error`
- `original-topic`
- `original-partition`
- `original-offset`

You can view dead-lettered messages in Kafka UI. The dead-letter producer waits at most 30 s for Kafka to confirm a write (`MessageTimeoutMs`); what happens after that is described under [Known limitations](#14-known-limitations-and-improvement-areas).

---

## 5. Redis dashboard projection

PostgreSQL is the source of truth. Redis holds only the dashboard and service-state projection derived from it, so it can be deleted and rebuilt at any time. `EventProcessor` and `EventService` never write to Redis; they only request a refresh. A single `RedisProjectionWorker` (a background service in the API) is the only writer.

**Rebuild:** the worker reads one consistent snapshot from PostgreSQL (a read-only `REPEATABLE READ` transaction with grouped counts and the latest event per service), computes the whole dashboard in memory, and writes it to Redis in one `MULTI/EXEC` with absolute values, so a rebuild can be repeated safely.

**When a rebuild runs:**
- at startup
- after a new event is stored or a status is changed. Refresh requests close together are combined into one rebuild.
- every 30 seconds (`DashboardProjection:RefreshIntervalSeconds`), as a safety net
- when Redis reconnects after an outage

A failed rebuild is retried after 1 s, 2 s, 4 s … up to 30 s. New refresh requests do not shorten this wait, so a steady event stream during a Redis outage does not cause a busy loop. A Redis reconnect does shorten it.

| Key | Type | Content |
|---|---|---|
| `dashboard:total` | string | Total number of events |
| `dashboard:critical` | string | CRITICAL events that are not RESOLVED |
| `dashboard:snapshotAt` | string | PostgreSQL time of the snapshot the projection was built from |
| `severity:{SEVERITY}:count` | string | Events per severity, all statuses, e.g. `severity:CRITICAL:count` |
| `status:{STATUS}:count` | string | Events per status, e.g. `status:OPEN:count` |
| `services` | set | Names of all services with events |
| `service:{service}:status` | hash | `lastEventTime`, `latestSeverity` (latest event by timestamp, then `eventId`), and `open:{SEVERITY}` (incidents per severity that are not RESOLVED) |

**Cleanup:** no key has a TTL. Each rebuild rewrites every key, zeros included, and deletes the keys of services that no longer have events, so nothing from an older projection survives.

**Recent events** are not kept in Redis. `/api/events/recent` reads PostgreSQL directly, so the `recent:events` list suggested in the assignment is not used and there is no second copy to keep consistent.

**Persistence:** Redis runs with the append-only file enabled and `noeviction`.

**Redis unavailable:** the API does not wait for Redis. Commands fail at once (`BacklogPolicy.FailFast`), the dashboard and services endpoints return `503`, and the worker retries until Redis is back.

---

## 6. PostgreSQL

One table, `events`, created by an EF Core migration ([`Data/Migrations`](backend/IncidentMonitoring.Infrastructure/Data/Migrations)).

| Column | Notes |
|---|---|
| `EventId` | Primary key, which **prevents duplicate events** |
| `Source`, `Service`, `Message` | Text |
| `Severity`, `Status` | Stored as text (`CRITICAL`, `OPEN`) |
| `Timestamp` | Event time (UTC, microsecond precision) |
| `ReceivedAt` | When the backend consumed the event |
| `StatusUpdatedAt` | Last status change through the API |

Indexes cover the default sort (`Timestamp`) and each list filter (`Severity`, `Status`, `Source`, `Service`).

PostgreSQL serves the event list, filtering, search, event details, recent events, the filter values, status updates, and the snapshot the Redis projection is built from.

**Concurrent status updates** use one conditional `UPDATE … WHERE EventId = @id AND Status = @expectedStatus`. If two requests change the same event at once, only one of them matches the row.

---

## 7. REST API

Base URL `http://localhost:8080`. Full, interactive documentation is available in Swagger. Request and response examples are in [docs/FRONTEND_INTEGRATION.md](docs/FRONTEND_INTEGRATION.md).

| Method | Endpoint | Description | Data from |
|---|---|---|---|
| GET | `/api/events` | Event list. Filters: `severity`, `status`, `source`, `service`, `search`. Paging: `page`, `pageSize` (max 100). Newest first. | PostgreSQL |
| GET | `/api/events/{eventId}` | One event | PostgreSQL |
| PUT | `/api/events/{eventId}/status` | Change status, body `{"status":"RESOLVED","expectedStatus":"OPEN"}` (`expectedStatus` optional) | PostgreSQL |
| GET | `/api/events/recent?count=10` | Latest events (1-50), ordered by `timestamp` then `eventId`, newest first | PostgreSQL |
| GET | `/api/events/facets` | Sources and services seen, plus all severities and statuses (for filter drop-downs) | PostgreSQL |
| GET | `/api/dashboard/summary` | Totals, distributions, service statuses, `snapshotAt` | Redis |
| GET | `/api/services` | Status per service | Redis |
| GET | `/health` | `Healthy` / `Unhealthy` | — |

Example: `GET /api/events?severity=CRITICAL&status=OPEN&source=ATS&search=route&page=1&pageSize=20`

### Status changes

| From | Allowed to |
|---|---|
| OPEN | ACKNOWLEDGED, RESOLVED |
| ACKNOWLEDGED | RESOLVED |
| RESOLVED | OPEN (reopen) |

Any other change returns `409 Conflict`. The rule is in [`StatusRules`](backend/IncidentMonitoring.Core/Rules/StatusRules.cs). Requesting the current status returns `200` and changes nothing.

A status update:
1. updates PostgreSQL with the conditional `UPDATE` described above
2. requests a dashboard refresh
3. pushes `eventUpdated` over SignalR

If another request changed the status first, the losing request returns `200` when the event already has the requested status, and `409` otherwise. Only the request that changed the row requests a refresh and sends `eventUpdated`.

`expectedStatus` is the status the client showed when the user chose the change. If the event has a different status now, the request returns `409` without changing anything, even when the change would be allowed from the new status. The dashboard always sends it. Requests without it are checked against the stored status only, as before. Requesting the status the event already has still returns `200`, whatever `expectedStatus` says.

The change is committed to PostgreSQL first, then a projection refresh is requested. Redis is not part of that transaction: the projection worker updates it asynchronously and sends `summaryUpdated` after a successful write.

### Service health

A service's status is derived from its **open incidents**, meaning its incidents that are not RESOLVED. The rule is in [`ServiceHealthRule`](backend/IncidentMonitoring.Core/Rules/ServiceHealthRule.cs):

| Worst open incident | Service status |
|---|---|
| CRITICAL | `DOWN` |
| MAJOR | `DEGRADED` |
| WARNING | `WARNING` |
| none, or only INFO | `HEALTHY` |

---

## 8. SignalR

Hub URL: `http://localhost:8080/hubs/incidents`. The server only pushes messages; clients do not call any hub methods.

| Message | Payload | Sent when |
|---|---|---|
| `eventReceived` | event (same JSON as the REST API) | A new event was stored in PostgreSQL |
| `eventUpdated` | event | An event's status was changed in PostgreSQL |
| `summaryUpdated` | dashboard summary (same JSON as `/api/dashboard/summary`) | The projection worker has written a new projection to Redis |

`summaryUpdated` contains exactly the projection that was just written. Several events can be combined into one summary, and it can arrive before or after the event message that caused it. While Redis is unavailable no summary is sent.

CORS allows `http://localhost:4200` with credentials, as the SignalR JavaScript client requires.

The dashboard's client is [`realtime.service.ts`](frontend/src/app/core/services/realtime.service.ts). It reconnects automatically and, because messages sent while it was offline are not replayed, the pages reload their data over REST after a reconnect. Details: [docs/FRONTEND_INTEGRATION.md](docs/FRONTEND_INTEGRATION.md).

---

## 9. Error handling

All errors are returned as ProblemDetails (`application/problem+json`):

| Status | When |
|---|---|
| 400 | Invalid query parameter or request body, e.g. unknown severity, `pageSize` > 100, missing status. Produced by ASP.NET Core model validation. |
| 404 | Event not found |
| 409 | Status change not allowed, lost to a concurrent change, or `expectedStatus` is no longer the current status |
| 503 | PostgreSQL temporarily unavailable, for the endpoints that use it; Redis unavailable, for the dashboard and services endpoints |
| 500 | Unexpected error. No internal details are returned; the exception is logged. |

The mapping from exception to status code is in one place: [`GlobalExceptionHandler`](backend/IncidentMonitoring.Api/Middleware/GlobalExceptionHandler.cs). The same rule, [`TransientFailure`](backend/IncidentMonitoring.Infrastructure/TransientFailure.cs), decides both when the API returns `503` for the database and when the Kafka consumer waits instead of dead-lettering a message.

## 10. Logging

The backend uses built-in .NET logging with message templates, so values such as `EventId`, `Partition` and `Offset` are separate fields. In Docker the console output is JSON (`Logging__Console__FormatterName=json`). Every Kafka outcome is logged (processed, duplicate, invalid, conflict, retry, dead-lettered, commit failure), as are status changes, PostgreSQL outages with the retry delay, projection rebuild failures and recovery, and unexpected API errors.

---

## 11. Testing

```bash
cd backend
dotnet test                                    # all 152 backend tests; Docker must be running
dotnet test IncidentMonitoring.Tests           # the 149 unit tests only, no Docker needed

cd ../frontend
npm test -- --watch=false                      # 15 Angular tests
```

**Unit tests (149):** PostgreSQL, Redis and SignalR are replaced by small in-memory fakes ([`Fakes.cs`](backend/IncidentMonitoring.Tests/Fakes.cs)).

| Test class | What it covers |
|---|---|
| `EventValidatorTests` | Required fields, lengths, eventId characters, severity and status values, strict ISO-8601 timestamps with a time zone, future-timestamp limit |
| `EventComparisonTests` | Which fields decide duplicate vs conflict; status, `ReceivedAt` and `StatusUpdatedAt` are ignored; microsecond precision |
| `EventProcessorTests` | A valid event is stored, a refresh is requested and `eventReceived` is sent once. Invalid, duplicate, conflict and status-different messages change nothing and send nothing. A database error is thrown to the consumer. |
| `EventServiceTests` | Status updates: winner, same-status no-op, 404, invalid transition, lost concurrent updates, and `expectedStatus` (matching, stale, missing, stale with the target already stored). Recent events ordering. |
| `RulesTests` | Allowed and forbidden status changes, service health rule |
| `DashboardProjectionTests` | Dashboard numbers built from a PostgreSQL snapshot (open, unresolved critical, per-service counts, latest event) |
| `DashboardServiceTests` | Summary read from Redis, and the summary built from a new projection |
| `RedisProjectionWorkerTests` | Startup, periodic and requested rebuilds; coalescing; retry after a failure; reconnect shortening the retry wait; `summaryUpdated` only after a successful write |
| `RefreshSignalTests` | The dirty flag and wake-up that coalesce refresh requests |
| `TransientFailureTests` | Which exceptions count as a temporary PostgreSQL outage (including the shapes seen when the database container stops), and that Redis and Kafka errors do not |
| `RetryBackoffTests`, `TimePrecisionTests`, `EventGeneratorTests` | Backoff sequence, microsecond truncation, generated event format |

**Integration tests (3):** [`EventRepositoryTests`](backend/IncidentMonitoring.IntegrationTests/EventRepositoryTests.cs) start a `postgres:16-alpine` container with Testcontainers and apply the real EF migrations. They cover what the fakes cannot:
- a duplicate `eventId` is rejected by the primary key, and the same `DbContext` can still read the stored row
- two concurrent conditional status updates: the second waits for the first one's row lock and then updates nothing
- the dashboard snapshot query: grouped counts, the latest event per service with `eventId` as tie-breaker, and the snapshot time

**Checked manually** against the running Docker stack (not covered by the automated tests): Redis and PostgreSQL outages and recovery, duplicate, conflict, invalid and malformed Kafka messages with the dead-letter topic, the 3-node Kafka cluster settings, and SignalR delivery.

A minimal GitHub Actions workflow ([.github/workflows/ci.yml](.github/workflows/ci.yml)) builds the solution, runs all backend tests (including the integration tests; the GitHub runner has Docker) and validates `docker-compose.yml`.

---

## 12. Assumptions

- **Open events** (`openEvents` in the summary) are events with status `OPEN`. A service's **open incident count** includes `OPEN` and `ACKNOWLEDGED`, i.e. everything not resolved, because an acknowledged incident still affects the service.
- **Critical events** (`criticalEvents`) are CRITICAL events that are not RESOLVED. `severityDistribution.CRITICAL` counts all CRITICAL events, so it can be higher.
- **Incoming status:** events can arrive with any status. Later changes are made through the API; a redelivered message never overwrites them.
- **Latest event of a service:** the event with the latest `timestamp`, with `eventId` as tie-breaker.
- **Duplicates:** the first event with a given `eventId` is kept. A later message with the same id and the same content is ignored; one with different content is a conflict and goes to the dead-letter topic.
- **Timestamps** are stored and returned in UTC, with microsecond precision (as in PostgreSQL). Late events are accepted; timestamps more than 5 minutes in the future are rejected.

## 13. Design decisions

- **Full rebuild instead of counter updates.** Updating Redis counters on every event cannot be repaired after a missed update; recomputing everything from PostgreSQL can. At this data volume a rebuild is cheap, and combining refresh requests keeps it to one rebuild per burst.
- **Duplicates are stopped by the PostgreSQL primary key.** A redelivered message is compared with the stored row, so no separate idempotency store is needed.
- **At-least-once processing.** The Kafka offset is committed only after a final outcome. A PostgreSQL outage is not treated as a bad message: the consumer waits and reads the same offset again.
- **Consumer and projection worker inside the API process.** One backend container keeps the deployment simple. Both are `BackgroundService`s.
- **UPPERCASE enum names** (`CRITICAL`, `OPEN`). They are exactly the schema values, so the same text is used in Kafka, JSON, Redis keys and the database without any mapping.

## 14. Known limitations and improvement areas

- **Single PostgreSQL instance.** While it is down, database endpoints return `503` and the consumer waits. Improvement: a replicated PostgreSQL setup.
- **Single Redis instance.** While it is down, events and status changes are still stored, but the dashboard and services endpoints return `503` until Redis is back and the projection is rebuilt. Improvement: Redis with failover.
- **One consumer loop.** Messages are handled one at a time, so a PostgreSQL outage pauses all assigned partitions, not only the one with the failing message. Improvement: per-partition processing.
- **Failed dead-letter write stops the consumer.** If Kafka does not confirm a dead-letter write within 30 s, the consumer stops without committing the source offset. Docker restarts the API and the message is processed again. Improvement: a bounded retry before stopping.
- **Dashboard REST reads are not one snapshot.** `/api/dashboard/summary` reads several Redis keys one after another, so a read during a rebuild can mix two consecutive projections. `summaryUpdated` always contains one complete projection.
- **Local Kafka setup.** Three combined broker/controller nodes on one machine show replication and failover for this assignment; they are not a production deployment.
- **No authentication or authorization.** Anyone who can reach the API can change statuses.
- **Search uses `ILIKE`.** Fine for this data volume. Improvement for large tables: a trigram or full-text index.
- **No dead-letter replay tool.** Dead-lettered messages can be inspected in Kafka UI but are not re-processed. Improvement: a replay command.

## 15. Screenshots

Taken from the running Docker Compose stack at http://localhost:4200. The screenshots below were captured in UTC+3.

**Dashboard**: summary cards, service health, severity distribution and the 10 most recent events. "Live" means the SignalR connection is up; "Last updated" is the time of the projection snapshot.

![Dashboard](docs/screenshots/dashboard.png)

**Events**: filtered to severity CRITICAL and status OPEN (9 of 129 events).

![Events filtered to CRITICAL and OPEN](docs/screenshots/events.png)

**Event detail**: an event whose status was changed from OPEN to ACKNOWLEDGED through the status endpoint. Only the allowed next status (Resolve) is offered.

![Event detail](docs/screenshots/event-detail.png)

**Service Status**: health, latest severity, open incident count (OPEN + ACKNOWLEDGED) and last event time per service.

![Service Status](docs/screenshots/services.png)
