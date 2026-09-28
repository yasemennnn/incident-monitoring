using IncidentMonitoring.Core.Dtos;
using IncidentMonitoring.Core.Models;

namespace IncidentMonitoring.Core.Interfaces;

// The business services depend on these interfaces so they can be unit tested without
// PostgreSQL, Redis or SignalR. Each has exactly one real implementation.

/// <summary>Durable event storage (PostgreSQL).</summary>
public interface IEventRepository
{
    /// <summary>Saves a new event. Returns false if an event with the same id already exists.</summary>
    Task<bool> AddAsync(IncidentEvent incidentEvent);

    /// <summary>Reads an event. It is not tracked: changes are made with <see cref="TryUpdateStatusAsync"/>.</summary>
    Task<IncidentEvent?> GetAsync(string eventId);

    /// <summary>The latest events by event time, newest first; EventId breaks ties.</summary>
    Task<List<IncidentEvent>> GetRecentAsync(int count);

    Task<PagedResult<IncidentEvent>> SearchAsync(EventFilter filter);

    Task<EventFacetsDto> GetFacetsAsync();

    /// <summary>
    /// Changes the status in one conditional UPDATE, only if it is still <paramref name="expectedStatus"/>.
    /// Returns false when no row matched: the event is gone or another request changed its status first.
    /// </summary>
    Task<bool> TryUpdateStatusAsync(string eventId, EventStatus expectedStatus, EventStatus newStatus, DateTime statusUpdatedAt);

    /// <summary>
    /// Reads the counts per service, status and severity, the latest event per service and the snapshot time,
    /// all from one consistent database snapshot.
    /// </summary>
    Task<DashboardSnapshot> GetDashboardSnapshotAsync();
}

/// <summary>
/// The dashboard projection in Redis. It is derived from PostgreSQL and written only by the projection worker.
/// </summary>
public interface IDashboardStore
{
    /// <summary>Totals, severity and status counts, unresolved CRITICAL count and snapshot time.</summary>
    Task<DashboardTotals> GetCountersAsync();

    Task<List<ServiceState>> GetServicesAsync();

    /// <summary>Replaces the whole dashboard with absolute values from one PostgreSQL snapshot.</summary>
    Task WriteSnapshotAsync(DashboardState state);
}

/// <summary>Asks for the Redis dashboard to be rebuilt from PostgreSQL.</summary>
public interface IDashboardRefresher
{
    /// <summary>
    /// Marks the dashboard as out of date and returns immediately; the rebuild runs in the background.
    /// Many requests close together result in one rebuild.
    /// </summary>
    void RequestRefresh();
}

/// <summary>Pushes changes to connected dashboards (SignalR). Sending is best effort: failures are not thrown.</summary>
public interface IEventNotifier
{
    /// <summary>A new event was stored.</summary>
    Task EventReceivedAsync(EventDto incidentEvent);

    /// <summary>An event's status was changed.</summary>
    Task EventUpdatedAsync(EventDto incidentEvent);

    /// <summary>A new dashboard projection was written; the summary is exactly the one written.</summary>
    Task SummaryUpdatedAsync(DashboardSummaryDto summary);
}
