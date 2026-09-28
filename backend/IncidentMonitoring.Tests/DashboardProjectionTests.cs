using IncidentMonitoring.Core.Models;
using IncidentMonitoring.Core.Rules;

namespace IncidentMonitoring.Tests;

public class DashboardProjectionTests
{
    private static readonly DateTime SnapshotAt = new(2026, 9, 28, 15, 0, 0, DateTimeKind.Utc);

    private static DashboardState Build(List<EventCountRow> counts, List<LatestServiceEvent>? latest = null) =>
        DashboardProjection.Build(new DashboardSnapshot(counts, latest ?? [], SnapshotAt));

    private static EventCountRow Row(string service, EventStatus status, Severity severity, long count) =>
        new(service, status, severity, count);

    // A. Empty database
    [Fact]
    public void Empty_snapshot_gives_zero_everywhere_and_no_services()
    {
        var dashboard = Build([]);

        Assert.Equal(0, dashboard.Counters.Total);
        Assert.All(Enum.GetValues<Severity>(), s => Assert.Equal(0, dashboard.Counters.BySeverity[s]));
        Assert.All(Enum.GetValues<EventStatus>(), s => Assert.Equal(0, dashboard.Counters.ByStatus[s]));
        Assert.Equal(0, dashboard.OpenEvents);
        Assert.Equal(0, dashboard.CriticalEvents);
        Assert.Empty(dashboard.Services);
        Assert.Equal(SnapshotAt, dashboard.SnapshotAt);
    }

    // B. Distribution
    [Fact]
    public void Totals_and_distributions_add_up_all_rows()
    {
        var dashboard = Build(
        [
            Row("route-service", EventStatus.OPEN, Severity.CRITICAL, 2),
            Row("route-service", EventStatus.RESOLVED, Severity.INFO, 5),
            Row("timetable-service", EventStatus.ACKNOWLEDGED, Severity.MAJOR, 3),
            Row("timetable-service", EventStatus.OPEN, Severity.WARNING, 1),
            Row("power-supply", EventStatus.RESOLVED, Severity.CRITICAL, 4)
        ]);

        Assert.Equal(15, dashboard.Counters.Total);

        Assert.Equal(5, dashboard.Counters.BySeverity[Severity.INFO]);
        Assert.Equal(1, dashboard.Counters.BySeverity[Severity.WARNING]);
        Assert.Equal(3, dashboard.Counters.BySeverity[Severity.MAJOR]);
        Assert.Equal(6, dashboard.Counters.BySeverity[Severity.CRITICAL]);

        Assert.Equal(3, dashboard.Counters.ByStatus[EventStatus.OPEN]);
        Assert.Equal(3, dashboard.Counters.ByStatus[EventStatus.ACKNOWLEDGED]);
        Assert.Equal(9, dashboard.Counters.ByStatus[EventStatus.RESOLVED]);
    }

    // C. OPEN semantics
    [Fact]
    public void Open_events_count_only_the_open_status()
    {
        var dashboard = Build(
        [
            Row("route-service", EventStatus.OPEN, Severity.INFO, 2),
            Row("route-service", EventStatus.ACKNOWLEDGED, Severity.INFO, 3),
            Row("route-service", EventStatus.RESOLVED, Severity.INFO, 4)
        ]);

        Assert.Equal(2, dashboard.OpenEvents);
    }

    // D. criticalEvents
    [Fact]
    public void Critical_events_are_the_unresolved_critical_ones()
    {
        var dashboard = Build(
        [
            Row("route-service", EventStatus.OPEN, Severity.CRITICAL, 1),
            Row("route-service", EventStatus.ACKNOWLEDGED, Severity.CRITICAL, 2),
            Row("route-service", EventStatus.RESOLVED, Severity.CRITICAL, 4),
            Row("route-service", EventStatus.OPEN, Severity.MAJOR, 8)
        ]);

        Assert.Equal(3, dashboard.CriticalEvents);
    }

    // E. Service unresolved counts
    [Fact]
    public void Service_open_counts_include_open_and_acknowledged_but_not_resolved()
    {
        var dashboard = Build(
        [
            Row("route-service", EventStatus.OPEN, Severity.MAJOR, 1),
            Row("route-service", EventStatus.ACKNOWLEDGED, Severity.MAJOR, 2),
            Row("route-service", EventStatus.RESOLVED, Severity.MAJOR, 5),
            Row("route-service", EventStatus.ACKNOWLEDGED, Severity.WARNING, 1)
        ]);

        var service = Assert.Single(dashboard.Services);
        Assert.Equal(3, service.OpenBySeverity[Severity.MAJOR]);
        Assert.Equal(1, service.OpenBySeverity[Severity.WARNING]);
        Assert.Equal(4, service.OpenBySeverity.Values.Sum());
        Assert.Equal(ServiceHealth.DEGRADED, ServiceHealthRule.Evaluate(service.OpenBySeverity));
    }

    // F. Resolved-only service
    [Fact]
    public void Service_with_only_resolved_events_is_listed_as_healthy()
    {
        var dashboard = Build(
            [Row("ventilation", EventStatus.RESOLVED, Severity.CRITICAL, 3)],
            [new LatestServiceEvent("ventilation", SnapshotAt.AddMinutes(-5), Severity.CRITICAL)]);

        var service = Assert.Single(dashboard.Services);
        Assert.Equal("ventilation", service.Name);
        Assert.All(Enum.GetValues<Severity>(), s => Assert.Equal(0, service.OpenBySeverity[s]));
        Assert.Equal(ServiceHealth.HEALTHY, ServiceHealthRule.Evaluate(service.OpenBySeverity));
    }

    // G. Latest metadata
    [Fact]
    public void Latest_time_and_severity_come_from_the_supplied_latest_row()
    {
        var latestTime = new DateTime(2026, 9, 28, 14, 59, 30, 123, DateTimeKind.Utc);
        var dashboard = Build(
            [
                Row("route-service", EventStatus.OPEN, Severity.CRITICAL, 1),
                Row("route-service", EventStatus.RESOLVED, Severity.INFO, 9),
                Row("timetable-service", EventStatus.OPEN, Severity.WARNING, 1)
            ],
            [
                new LatestServiceEvent("route-service", latestTime, Severity.INFO),
                new LatestServiceEvent("timetable-service", latestTime.AddMinutes(-1), Severity.WARNING)
            ]);

        var route = dashboard.Services.Single(s => s.Name == "route-service");
        Assert.Equal(latestTime, route.LastEventTime);
        Assert.Equal(Severity.INFO, route.LatestSeverity);

        var timetable = dashboard.Services.Single(s => s.Name == "timetable-service");
        Assert.Equal(latestTime.AddMinutes(-1), timetable.LastEventTime);
        Assert.Equal(Severity.WARNING, timetable.LatestSeverity);
    }

    // H. Zero filling
    [Fact]
    public void Missing_enum_values_are_present_as_zero()
    {
        var dashboard = Build([Row("route-service", EventStatus.OPEN, Severity.WARNING, 2)]);

        Assert.Equal(Enum.GetValues<Severity>().Length, dashboard.Counters.BySeverity.Count);
        Assert.Equal(Enum.GetValues<EventStatus>().Length, dashboard.Counters.ByStatus.Count);
        Assert.Equal(0, dashboard.Counters.BySeverity[Severity.CRITICAL]);
        Assert.Equal(0, dashboard.Counters.ByStatus[EventStatus.RESOLVED]);

        var service = Assert.Single(dashboard.Services);
        Assert.Equal(Enum.GetValues<Severity>().Length, service.OpenBySeverity.Count);
        Assert.Equal(2, service.OpenBySeverity[Severity.WARNING]);
        Assert.Equal(0, service.OpenBySeverity[Severity.INFO]);
    }

    [Fact]
    public void Services_are_ordered_by_name()
    {
        var dashboard = Build(
        [
            Row("zone-controller", EventStatus.OPEN, Severity.INFO, 1),
            Row("route-service", EventStatus.OPEN, Severity.INFO, 1),
            Row("passenger-info", EventStatus.OPEN, Severity.INFO, 1)
        ]);

        Assert.Equal(["passenger-info", "route-service", "zone-controller"], dashboard.Services.Select(s => s.Name));
    }
}
