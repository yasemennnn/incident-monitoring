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

    Task<IncidentEvent?> GetAsync(string eventId);

    Task<List<IncidentEvent>> GetByIdsAsync(List<string> eventIds);

    Task<PagedResult<IncidentEvent>> SearchAsync(EventFilter filter);

    Task<EventFacetsDto> GetFacetsAsync();

    /// <summary>Persists changes made to events loaded with <see cref="GetAsync"/>.</summary>
    Task SaveChangesAsync();
}

/// <summary>Live dashboard state (Redis).</summary>
public interface IDashboardStore
{
    /// <summary>Updates counters, service state and the recent list for a new event.</summary>
    Task AddEventAsync(IncidentEvent incidentEvent);

    /// <summary>Moves the status counters from the old to the new status.</summary>
    Task UpdateStatusAsync(IncidentEvent incidentEvent, EventStatus oldStatus);

    Task<DashboardCounters> GetCountersAsync();

    Task<List<ServiceState>> GetServicesAsync();

    /// <summary>Ids of the most recently received events, newest first.</summary>
    Task<List<string>> GetRecentEventIdsAsync(int count);
}

/// <summary>Pushes changes to connected dashboards (SignalR).</summary>
public interface IEventNotifier
{
    Task EventReceivedAsync(EventDto incidentEvent);

    Task EventUpdatedAsync(EventDto incidentEvent);
}
