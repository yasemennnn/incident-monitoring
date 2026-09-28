using IncidentMonitoring.Core.Models;
using IncidentMonitoring.Core.Rules;

namespace IncidentMonitoring.Tests;

public class EventComparisonTests
{
    private static IncidentEvent Event() => new()
    {
        EventId = "EVT-1",
        Source = "ATS",
        Service = "route-service",
        Severity = Severity.CRITICAL,
        Message = "Route locking failed",
        Status = EventStatus.OPEN,
        Timestamp = new DateTime(2026, 9, 28, 14, 35, 12, 123, DateTimeKind.Utc),
        ReceivedAt = new DateTime(2026, 9, 28, 14, 35, 13, DateTimeKind.Utc),
        StatusUpdatedAt = null
    };

    private static List<string> Compare(Action<IncidentEvent> changeIncoming)
    {
        var incoming = Event();
        changeIncoming(incoming);
        return EventComparison.DifferingImmutableFields(Event(), incoming);
    }

    [Fact]
    public void Identical_events_have_no_differences()
    {
        Assert.Empty(Compare(_ => { }));
    }

    [Fact]
    public void Different_source_is_reported()
    {
        Assert.Equal(["source"], Compare(e => e.Source = "SCADA"));
    }

    [Fact]
    public void Different_service_is_reported()
    {
        Assert.Equal(["service"], Compare(e => e.Service = "timetable-service"));
    }

    [Fact]
    public void Different_severity_is_reported()
    {
        Assert.Equal(["severity"], Compare(e => e.Severity = Severity.INFO));
    }

    [Fact]
    public void Different_message_is_reported()
    {
        Assert.Equal(["message"], Compare(e => e.Message = "Configuration reloaded"));
    }

    [Fact]
    public void Different_timestamp_is_reported()
    {
        Assert.Equal(["timestamp"], Compare(e => e.Timestamp = e.Timestamp.AddMilliseconds(1)));
    }

    [Fact]
    public void Several_differences_are_reported_in_a_fixed_order()
    {
        var differences = Compare(e =>
        {
            e.Timestamp = e.Timestamp.AddSeconds(1);
            e.Source = "SCADA";
            e.Message = "other";
        });

        Assert.Equal(["source", "message", "timestamp"], differences);
    }

    [Fact]
    public void Status_is_ignored()
    {
        Assert.Empty(Compare(e => e.Status = EventStatus.ACKNOWLEDGED));
    }

    [Fact]
    public void Status_updated_at_is_ignored()
    {
        Assert.Empty(Compare(e => e.StatusUpdatedAt = DateTime.UtcNow));
    }

    [Fact]
    public void Received_at_is_ignored()
    {
        Assert.Empty(Compare(e => e.ReceivedAt = e.ReceivedAt.AddMinutes(5)));
    }

    [Fact]
    public void Timestamps_equal_at_microsecond_precision_are_equal()
    {
        // 700 ns more than the stored value: below PostgreSQL's precision, so it is the same timestamp.
        Assert.Empty(Compare(e => e.Timestamp = e.Timestamp.AddTicks(7)));
    }
}
