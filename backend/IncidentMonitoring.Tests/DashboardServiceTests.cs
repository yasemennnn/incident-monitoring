using IncidentMonitoring.Core.Dtos;
using IncidentMonitoring.Core.Models;
using IncidentMonitoring.Core.Rules;
using IncidentMonitoring.Core.Services;

namespace IncidentMonitoring.Tests;

public class DashboardServiceTests
{
    private static readonly DateTime SnapshotAt = new(2026, 9, 28, 15, 0, 0, DateTimeKind.Utc);

    // REST: the summary is read from the projection values stored in Redis.
    [Fact]
    public async Task Summary_is_read_from_the_projection_in_redis()
    {
        var store = new FakeDashboardStore
        {
            Totals = new DashboardTotals(
                new DashboardCounters(
                    Total: 10,
                    BySeverity: new() { [Severity.INFO] = 5, [Severity.WARNING] = 1, [Severity.MAJOR] = 1, [Severity.CRITICAL] = 3 },
                    ByStatus: new() { [EventStatus.OPEN] = 4, [EventStatus.ACKNOWLEDGED] = 2, [EventStatus.RESOLVED] = 4 }),
                CriticalEvents: 1,
                SnapshotAt: SnapshotAt),
            Services =
            [
                new ServiceState("route-service", SnapshotAt.AddMinutes(-1), Severity.CRITICAL,
                    new() { [Severity.CRITICAL] = 1, [Severity.INFO] = 2 }),
                new ServiceState("timetable-service", SnapshotAt.AddMinutes(-2), Severity.INFO,
                    new() { [Severity.INFO] = 0 })
            ]
        };

        var summary = await new DashboardService(store).GetSummaryAsync();

        Assert.Equal(10, summary.TotalEvents);
        Assert.Equal(4, summary.OpenEvents);
        Assert.Equal(1, summary.CriticalEvents);                           // from dashboard:critical
        Assert.Equal(3, summary.SeverityDistribution[Severity.CRITICAL]); // all CRITICAL events: intentionally higher
        Assert.Equal(4, summary.StatusDistribution[EventStatus.RESOLVED]);
        Assert.Equal(SnapshotAt, summary.SnapshotAt);

        var route = summary.Services.Single(s => s.Name == "route-service");
        Assert.Equal(ServiceHealth.DOWN, route.Status);
        Assert.Equal(3, route.OpenIncidentCount);
        Assert.Equal(ServiceHealth.HEALTHY, summary.Services.Single(s => s.Name == "timetable-service").Status);
    }

    [Fact]
    public async Task Snapshot_time_is_null_before_the_first_projection()
    {
        var summary = await new DashboardService(new FakeDashboardStore()).GetSummaryAsync();

        Assert.Null(summary.SnapshotAt);
        Assert.Equal(0, summary.TotalEvents);
    }

    // SignalR: the summary is built from the DashboardState the worker has just written.
    [Fact]
    public void Summary_of_a_built_projection_uses_the_frozen_dashboard_semantics()
    {
        var state = DashboardProjection.Build(new DashboardSnapshot(
            [
                new EventCountRow("route-service", EventStatus.OPEN, Severity.CRITICAL, 1),
                new EventCountRow("route-service", EventStatus.ACKNOWLEDGED, Severity.CRITICAL, 2),
                new EventCountRow("route-service", EventStatus.RESOLVED, Severity.CRITICAL, 4),
                new EventCountRow("timetable-service", EventStatus.ACKNOWLEDGED, Severity.WARNING, 1)
            ],
            [
                new LatestServiceEvent("route-service", SnapshotAt.AddMinutes(-1), Severity.CRITICAL),
                new LatestServiceEvent("timetable-service", SnapshotAt.AddMinutes(-3), Severity.WARNING)
            ],
            SnapshotAt));

        var summary = DashboardSummaryDto.From(state);

        Assert.Equal(8, summary.TotalEvents);
        Assert.Equal(1, summary.OpenEvents);                               // OPEN only
        Assert.Equal(3, summary.CriticalEvents);                           // OPEN + ACKNOWLEDGED CRITICAL
        Assert.Equal(7, summary.SeverityDistribution[Severity.CRITICAL]); // RESOLVED CRITICAL included here
        Assert.Equal(3, summary.StatusDistribution[EventStatus.ACKNOWLEDGED]);
        Assert.Equal(SnapshotAt, summary.SnapshotAt);

        var route = summary.Services.Single(s => s.Name == "route-service");
        Assert.Equal(3, route.OpenIncidentCount);                          // OPEN + ACKNOWLEDGED, not RESOLVED
        Assert.Equal(ServiceHealth.DOWN, route.Status);
        Assert.Equal(SnapshotAt.AddMinutes(-1), route.LastEventTime);
        Assert.Equal(Severity.CRITICAL, route.LatestSeverity);

        var timetable = summary.Services.Single(s => s.Name == "timetable-service");
        Assert.Equal(1, timetable.OpenIncidentCount);
        Assert.Equal(ServiceHealth.WARNING, timetable.Status);
    }
}
