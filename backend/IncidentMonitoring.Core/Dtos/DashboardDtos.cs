using IncidentMonitoring.Core.Models;

namespace IncidentMonitoring.Core.Dtos;

/// <summary>Response of GET /api/dashboard/summary (also pushed as SignalR "summaryUpdated").</summary>
public record DashboardSummaryDto(
    long TotalEvents,
    long OpenEvents,
    long CriticalEvents,
    Dictionary<Severity, long> SeverityDistribution,
    Dictionary<EventStatus, long> StatusDistribution,
    List<ServiceStatusDto> Services);

/// <summary>Current state of one service (GET /api/services).</summary>
public record ServiceStatusDto(
    string Name,
    ServiceHealth Status,
    DateTime? LastEventTime,
    Severity? LatestSeverity,
    long OpenIncidentCount);
