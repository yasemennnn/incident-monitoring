using IncidentMonitoring.Core.Dtos;
using IncidentMonitoring.Core.Interfaces;
using IncidentMonitoring.Core.Models;

namespace IncidentMonitoring.Tests;

// Simple in-memory replacements for PostgreSQL, Redis and SignalR.

public class FakeEventRepository : IEventRepository
{
    /// <summary>The "database" rows.</summary>
    public Dictionary<string, IncidentEvent> Events { get; } = [];
    public bool ThrowOnAdd { get; set; }

    /// <summary>AddAsync reports the id as already stored without storing anything, so the row cannot be loaded.</summary>
    public bool ReportDuplicateWithoutRow { get; set; }

    /// <summary>Every conditional status update that was attempted, whether it matched or not.</summary>
    public List<(string EventId, EventStatus Expected, EventStatus New, DateTime At)> StatusUpdates { get; } = [];

    /// <summary>Runs right before a conditional update is evaluated, to simulate another request winning first.</summary>
    public Action? BeforeStatusUpdate { get; set; }

    public Task<bool> AddAsync(IncidentEvent incidentEvent)
    {
        if (ThrowOnAdd)
            throw new InvalidOperationException("database unavailable");
        if (ReportDuplicateWithoutRow)
            return Task.FromResult(false);
        return Task.FromResult(Events.TryAdd(incidentEvent.EventId, incidentEvent));
    }

    // Returns a copy, like an untracked EF query: changing it does not change the stored row.
    public Task<IncidentEvent?> GetAsync(string eventId) =>
        Task.FromResult(Events.TryGetValue(eventId, out var e) ? Copy(e) : null);

    public Task<bool> TryUpdateStatusAsync(string eventId, EventStatus expectedStatus, EventStatus newStatus, DateTime statusUpdatedAt)
    {
        StatusUpdates.Add((eventId, expectedStatus, newStatus, statusUpdatedAt));
        BeforeStatusUpdate?.Invoke();

        // Same condition as the SQL: WHERE EventId = @id AND Status = @expected
        if (!Events.TryGetValue(eventId, out var row) || row.Status != expectedStatus)
            return Task.FromResult(false);

        row.Status = newStatus;
        row.StatusUpdatedAt = statusUpdatedAt;
        return Task.FromResult(true);
    }

    // Same order as the SQL query (EventId compared ordinally here; PostgreSQL uses the column collation).
    public Task<List<IncidentEvent>> GetRecentAsync(int count) =>
        Task.FromResult(Events.Values
            .OrderByDescending(e => e.Timestamp)
            .ThenByDescending(e => e.EventId, StringComparer.Ordinal)
            .Take(count)
            .Select(Copy)
            .ToList());

    public Task<PagedResult<IncidentEvent>> SearchAsync(EventFilter filter) =>
        Task.FromResult(new PagedResult<IncidentEvent>(Events.Values.ToList(), 1, 20, Events.Count));

    public Task<EventFacetsDto> GetFacetsAsync() => Task.FromResult(new EventFacetsDto([], [], [], []));

    /// <summary>What GetDashboardSnapshotAsync returns (the real query is PostgreSQL-specific).</summary>
    public DashboardSnapshot Snapshot { get; set; } = new([], [], new DateTime(2026, 9, 28, 15, 0, 0, DateTimeKind.Utc));

    /// <summary>Awaited at the start of every snapshot read, to hold a rebuild in the middle.</summary>
    public Func<Task>? BeforeSnapshot { get; set; }

    private int _snapshotReads;
    public int SnapshotReads => Volatile.Read(ref _snapshotReads);

    public async Task<DashboardSnapshot> GetDashboardSnapshotAsync()
    {
        Interlocked.Increment(ref _snapshotReads);
        if (BeforeSnapshot is not null)
            await BeforeSnapshot();
        return Snapshot;
    }

    private static IncidentEvent Copy(IncidentEvent e) => new()
    {
        EventId = e.EventId,
        Source = e.Source,
        Service = e.Service,
        Severity = e.Severity,
        Message = e.Message,
        Status = e.Status,
        Timestamp = e.Timestamp,
        ReceivedAt = e.ReceivedAt,
        StatusUpdatedAt = e.StatusUpdatedAt
    };
}

public class FakeDashboardStore : IDashboardStore
{
    /// <summary>
    /// What the read methods return, as if a projection had been written earlier. The default is an empty Redis:
    /// like the real store, every severity and status is present with 0, and there is no snapshot time.
    /// </summary>
    public DashboardTotals Totals { get; set; } = new(
        new DashboardCounters(
            0,
            Enum.GetValues<Severity>().ToDictionary(s => s, _ => 0L),
            Enum.GetValues<EventStatus>().ToDictionary(s => s, _ => 0L)),
        0,
        null);
    public List<ServiceState> Services { get; set; } = [];

    public Task<DashboardTotals> GetCountersAsync() => Task.FromResult(Totals);

    public Task<List<ServiceState>> GetServicesAsync() => Task.FromResult(Services);

    // Written by the projection worker on its own thread, so access is locked.
    private readonly List<DashboardState> _snapshots = [];
    private int _snapshotWriteAttempts;

    /// <summary>How many of the next snapshot writes fail, like a Redis outage.</summary>
    public int FailingSnapshotWrites { get; set; }

    public int SnapshotWriteAttempts => Volatile.Read(ref _snapshotWriteAttempts);

    /// <summary>Runs inside a write that is about to fail, e.g. to simulate Redis reconnecting during the attempt.</summary>
    public Action? BeforeFailingWrite { get; set; }

    public List<DashboardState> Snapshots
    {
        get { lock (_snapshots) return [.. _snapshots]; }
    }

    public Task WriteSnapshotAsync(DashboardState state)
    {
        Interlocked.Increment(ref _snapshotWriteAttempts);
        bool fail;
        lock (_snapshots)
        {
            fail = FailingSnapshotWrites > 0;
            if (fail)
                FailingSnapshotWrites--;
            else
                _snapshots.Add(state);
        }

        if (!fail)
            return Task.CompletedTask;
        BeforeFailingWrite?.Invoke();
        throw new InvalidOperationException("redis unavailable");
    }
}

/// <summary>Counts refresh requests; the mutation paths must only ever call RequestRefresh.</summary>
public class FakeDashboardRefresher : IDashboardRefresher
{
    public int Requests { get; private set; }

    public void RequestRefresh() => Requests++;
}

/// <summary>A clock that always returns the same time, so "now" is predictable in tests.</summary>
public class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

public class FakeNotifier : IEventNotifier
{
    public List<EventDto> Received { get; } = [];
    public List<EventDto> Updated { get; } = [];

    // Summaries are sent from the projection worker's thread, so access is locked.
    private readonly List<DashboardSummaryDto> _summaries = [];

    public List<DashboardSummaryDto> Summaries
    {
        get { lock (_summaries) return [.. _summaries]; }
    }

    /// <summary>SummaryUpdatedAsync throws, like a broken notifier (the real one logs instead of throwing).</summary>
    public bool FailSummaries { get; set; }

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

    public Task SummaryUpdatedAsync(DashboardSummaryDto summary)
    {
        if (FailSummaries)
            throw new InvalidOperationException("signalr unavailable");
        lock (_summaries)
            _summaries.Add(summary);
        return Task.CompletedTask;
    }
}
