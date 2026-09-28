namespace IncidentMonitoring.Core.Models;

/// <summary>Dashboard counters as stored in Redis.</summary>
public record DashboardCounters(
    long Total,
    Dictionary<Severity, long> BySeverity,
    Dictionary<EventStatus, long> ByStatus);

/// <summary>The dashboard numbers as read back from Redis.</summary>
/// <param name="CriticalEvents">CRITICAL events that are not RESOLVED.</param>
/// <param name="SnapshotAt">PostgreSQL time of the projection's snapshot; null until the first projection is written.</param>
public record DashboardTotals(DashboardCounters Counters, long CriticalEvents, DateTime? SnapshotAt);

/// <summary>Live state of one service as stored in Redis.</summary>
/// <param name="OpenBySeverity">Number of open (not RESOLVED) incidents per severity.</param>
public record ServiceState(
    string Name,
    DateTime? LastEventTime,
    Severity? LatestSeverity,
    Dictionary<Severity, long> OpenBySeverity);
