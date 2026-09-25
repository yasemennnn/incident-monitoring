using IncidentMonitoring.Core.Models;
using IncidentMonitoring.Core.Rules;

namespace IncidentMonitoring.Tests;

public class RulesTests
{
    [Theory]
    [InlineData(EventStatus.OPEN, EventStatus.ACKNOWLEDGED, true)]
    [InlineData(EventStatus.OPEN, EventStatus.RESOLVED, true)]
    [InlineData(EventStatus.ACKNOWLEDGED, EventStatus.RESOLVED, true)]
    [InlineData(EventStatus.RESOLVED, EventStatus.OPEN, true)]
    [InlineData(EventStatus.ACKNOWLEDGED, EventStatus.OPEN, false)]
    [InlineData(EventStatus.RESOLVED, EventStatus.ACKNOWLEDGED, false)]
    [InlineData(EventStatus.OPEN, EventStatus.OPEN, false)]
    public void Status_change_rules(EventStatus from, EventStatus to, bool allowed)
    {
        Assert.Equal(allowed, StatusRules.CanChange(from, to));
    }

    [Theory]
    [InlineData(0, 0, 0, 0, ServiceHealth.HEALTHY)]
    [InlineData(3, 0, 0, 0, ServiceHealth.HEALTHY)]
    [InlineData(0, 1, 0, 0, ServiceHealth.WARNING)]
    [InlineData(0, 1, 1, 0, ServiceHealth.DEGRADED)]
    [InlineData(0, 1, 1, 1, ServiceHealth.DOWN)]
    public void Service_health_follows_worst_open_severity(long info, long warning, long major, long critical, ServiceHealth expected)
    {
        var open = new Dictionary<Severity, long>
        {
            [Severity.INFO] = info,
            [Severity.WARNING] = warning,
            [Severity.MAJOR] = major,
            [Severity.CRITICAL] = critical
        };

        Assert.Equal(expected, ServiceHealthRule.Evaluate(open));
    }
}
