using IncidentMonitoring.Core.Models;
using IncidentMonitoring.Core.Services;

namespace IncidentMonitoring.Tests;

public class DashboardServiceTests
{
    [Fact]
    public async Task Summary_is_built_from_redis_counters_and_service_state()
    {
        var store = new FakeDashboardStore
        {
            Counters = new DashboardCounters(
                Total: 10,
                BySeverity: new() { [Severity.INFO] = 6, [Severity.WARNING] = 2, [Severity.MAJOR] = 1, [Severity.CRITICAL] = 1 },
                ByStatus: new() { [EventStatus.OPEN] = 7, [EventStatus.ACKNOWLEDGED] = 1, [EventStatus.RESOLVED] = 2 }),
            Services =
            [
                new ServiceState("route-service", DateTime.UtcNow, Severity.CRITICAL,
                    new() { [Severity.CRITICAL] = 1, [Severity.INFO] = 2 }),
                new ServiceState("timetable-service", DateTime.UtcNow, Severity.INFO,
                    new() { [Severity.INFO] = 3 })
            ]
        };

        var summary = await new DashboardService(store).GetSummaryAsync();

        Assert.Equal(10, summary.TotalEvents);
        Assert.Equal(7, summary.OpenEvents);
        Assert.Equal(1, summary.CriticalEvents);

        var route = summary.Services.Single(s => s.Name == "route-service");
        Assert.Equal(ServiceHealth.DOWN, route.Status);
        Assert.Equal(3, route.OpenIncidentCount);
        Assert.Equal(ServiceHealth.HEALTHY, summary.Services.Single(s => s.Name == "timetable-service").Status);
    }
}
