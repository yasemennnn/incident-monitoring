using IncidentMonitoring.Core.Models;
using IncidentMonitoring.Core.Rules;

namespace IncidentMonitoring.Core.Dtos;

/// <summary>Response of GET /api/dashboard/summary (also pushed as SignalR "summaryUpdated").</summary>
/// <param name="OpenEvents">Events whose status is OPEN (ACKNOWLEDGED is not included).</param>
/// <param name="CriticalEvents">
/// CRITICAL events that are not RESOLVED. SeverityDistribution[CRITICAL] counts all CRITICAL events, so it can be higher.
/// </param>
/// <param name="SnapshotAt">PostgreSQL time of the snapshot these numbers come from; null before the first rebuild.</param>
public record DashboardSummaryDto(
    long TotalEvents,
    long OpenEvents,
    long CriticalEvents,
    Dictionary<Severity, long> SeverityDistribution,
    Dictionary<EventStatus, long> StatusDistribution,
    List<ServiceStatusDto> Services,
    DateTime? SnapshotAt)
{
    /// <summary>The summary of a projection the worker has just built.</summary>
    public static DashboardSummaryDto From(DashboardState state) =>
        From(state.Counters, state.CriticalEvents, state.SnapshotAt, state.Services);

    public static DashboardSummaryDto From(DashboardCounters counters, long criticalEvents, DateTime? snapshotAt, List<ServiceState> services) => new(
        TotalEvents: counters.Total,
        OpenEvents: counters.ByStatus[EventStatus.OPEN],
        CriticalEvents: criticalEvents,
        SeverityDistribution: counters.BySeverity,
        StatusDistribution: counters.ByStatus,
        Services: services.Select(ServiceStatusDto.From).ToList(),
        SnapshotAt: snapshotAt);
}

/// <summary>Current state of one service (GET /api/services).</summary>
/// <param name="OpenIncidentCount">Events of this service that are not RESOLVED (OPEN and ACKNOWLEDGED).</param>
public record ServiceStatusDto(
    string Name,
    ServiceHealth Status,
    DateTime? LastEventTime,
    Severity? LatestSeverity,
    long OpenIncidentCount)
{
    public static ServiceStatusDto From(ServiceState s) => new(
        Name: s.Name,
        Status: ServiceHealthRule.Evaluate(s.OpenBySeverity),
        LastEventTime: s.LastEventTime,
        LatestSeverity: s.LatestSeverity,
        OpenIncidentCount: s.OpenBySeverity.Values.Sum());
}
