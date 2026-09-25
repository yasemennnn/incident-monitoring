using IncidentMonitoring.Core.Dtos;
using IncidentMonitoring.Core.Interfaces;
using IncidentMonitoring.Core.Models;

namespace IncidentMonitoring.Tests;

// Simple in-memory replacements for PostgreSQL, Redis and SignalR.

public class FakeEventRepository : IEventRepository
{
    public Dictionary<string, IncidentEvent> Events { get; } = [];
    public int SaveChangesCount { get; private set; }
    public bool ThrowOnAdd { get; set; }

    public Task<bool> AddAsync(IncidentEvent incidentEvent)
    {
        if (ThrowOnAdd)
            throw new InvalidOperationException("database unavailable");
        return Task.FromResult(Events.TryAdd(incidentEvent.EventId, incidentEvent));
    }

    public Task<IncidentEvent?> GetAsync(string eventId) => Task.FromResult(Events.GetValueOrDefault(eventId));

    public Task<List<IncidentEvent>> GetByIdsAsync(List<string> eventIds) =>
        Task.FromResult(eventIds.Where(Events.ContainsKey).Select(id => Events[id]).ToList());

    public Task<PagedResult<IncidentEvent>> SearchAsync(EventFilter filter) =>
        Task.FromResult(new PagedResult<IncidentEvent>(Events.Values.ToList(), 1, 20, Events.Count));

    public Task<EventFacetsDto> GetFacetsAsync() => Task.FromResult(new EventFacetsDto([], [], [], []));

    public Task SaveChangesAsync()
    {
        SaveChangesCount++;
        return Task.CompletedTask;
    }
}

public class FakeDashboardStore : IDashboardStore
{
    public List<string> AddedEventIds { get; } = [];
    public List<(EventStatus From, EventStatus To)> StatusChanges { get; } = [];
    public bool IsDown { get; set; }
    public DashboardCounters Counters { get; set; } = new(0, [], []);
    public List<ServiceState> Services { get; set; } = [];

    public Task AddEventAsync(IncidentEvent incidentEvent)
    {
        ThrowIfDown();
        AddedEventIds.Add(incidentEvent.EventId);
        return Task.CompletedTask;
    }

    public Task UpdateStatusAsync(IncidentEvent incidentEvent, EventStatus oldStatus)
    {
        ThrowIfDown();
        StatusChanges.Add((oldStatus, incidentEvent.Status));
        return Task.CompletedTask;
    }

    public Task<DashboardCounters> GetCountersAsync() => Task.FromResult(Counters);

    public Task<List<ServiceState>> GetServicesAsync() => Task.FromResult(Services);

    public Task<List<string>> GetRecentEventIdsAsync(int count) => Task.FromResult(AddedEventIds.Take(count).ToList());

    private void ThrowIfDown()
    {
        if (IsDown)
            throw new InvalidOperationException("redis unavailable");
    }
}

public class FakeNotifier : IEventNotifier
{
    public List<EventDto> Received { get; } = [];
    public List<EventDto> Updated { get; } = [];

    public Task EventReceivedAsync(EventDto incidentEvent)
    {
        Received.Add(incidentEvent);
        return Task.CompletedTask;
    }

    public Task EventUpdatedAsync(EventDto incidentEvent)
    {
        Updated.Add(incidentEvent);
        return Task.CompletedTask;
    }
}
