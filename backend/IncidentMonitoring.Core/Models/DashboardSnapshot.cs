namespace IncidentMonitoring.Core.Models;

/// <summary>Number of events with one combination of service, status and severity (PostgreSQL GROUP BY row).</summary>
public record EventCountRow(string Service, EventStatus Status, Severity Severity, long Count);

/// <summary>The latest event of one service, by Timestamp and then EventId.</summary>
public record LatestServiceEvent(string Service, DateTime Timestamp, Severity Severity);

/// <summary>Everything the dashboard is built from, read in one PostgreSQL snapshot.</summary>
/// <param name="SnapshotAt">PostgreSQL time of the snapshot (UTC).</param>
public record DashboardSnapshot(List<EventCountRow> Counts, List<LatestServiceEvent> Latest, DateTime SnapshotAt);

/// <summary>The complete dashboard built from one <see cref="DashboardSnapshot"/>: absolute values, not deltas.</summary>
/// <param name="CriticalEvents">CRITICAL events that are not RESOLVED.</param>
public record DashboardState(
    DashboardCounters Counters,
    long CriticalEvents,
    List<ServiceState> Services,
    DateTime SnapshotAt)
{
    /// <summary>Events whose status is OPEN; ACKNOWLEDGED events are not counted.</summary>
    public long OpenEvents => Counters.ByStatus[EventStatus.OPEN];
}
