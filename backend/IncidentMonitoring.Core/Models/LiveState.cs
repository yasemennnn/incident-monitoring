namespace IncidentMonitoring.Core.Models;

/// <summary>Dashboard counters as stored in Redis.</summary>
public record DashboardCounters(
    long Total,
    Dictionary<Severity, long> BySeverity,
    Dictionary<EventStatus, long> ByStatus);

/// <summary>Live state of one service as stored in Redis.</summary>
/// <param name="OpenBySeverity">Number of open (not RESOLVED) incidents per severity.</param>
public record ServiceState(
    string Name,
    DateTime? LastEventTime,
    Severity? LatestSeverity,
    Dictionary<Severity, long> OpenBySeverity);
