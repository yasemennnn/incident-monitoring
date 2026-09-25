using IncidentMonitoring.Core.Dtos;
using IncidentMonitoring.Core.Models;
using IncidentMonitoring.Core.Rules;

namespace IncidentMonitoring.Tests;

public class EventValidatorTests
{
    private static readonly EventMessage ValidMessage = new(
        "EVT-10001", "ATS", "route-service", "CRITICAL", "Route locking failed", "OPEN", "2026-06-01T10:15:00Z");

    [Fact]
    public void Valid_message_becomes_an_event()
    {
        var result = EventValidator.Validate(ValidMessage);

        Assert.True(result.IsValid);
        Assert.Equal("EVT-10001", result.Event!.EventId);
        Assert.Equal(Severity.CRITICAL, result.Event.Severity);
        Assert.Equal(EventStatus.OPEN, result.Event.Status);
        Assert.Equal(new DateTime(2026, 6, 1, 10, 15, 0, DateTimeKind.Utc), result.Event.Timestamp);
        Assert.Equal(DateTimeKind.Utc, result.Event.Timestamp.Kind);
    }

    [Fact]
    public void Every_missing_field_is_reported()
    {
        var result = EventValidator.Validate(new EventMessage(null, null, null, null, null, null, null));

        Assert.False(result.IsValid);
        Assert.Null(result.Event);
        Assert.Equal(7, result.Errors.Count);
    }

    [Theory]
    [InlineData("FATAL", "OPEN")]
    [InlineData("2", "OPEN")]
    [InlineData("CRITICAL", "CLOSED")]
    public void Unknown_severity_or_status_is_rejected(string severity, string status)
    {
        var result = EventValidator.Validate(ValidMessage with { Severity = severity, Status = status });

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Invalid_timestamp_is_rejected()
    {
        var result = EventValidator.Validate(ValidMessage with { Timestamp = "yesterday" });

        Assert.Contains(result.Errors, e => e.StartsWith("timestamp"));
    }
}
