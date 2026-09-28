using System.Globalization;
using IncidentMonitoring.Core.Dtos;
using IncidentMonitoring.Core.Models;
using IncidentMonitoring.Core.Rules;

namespace IncidentMonitoring.Tests;

public class EventValidatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 28, 14, 35, 12, 123, TimeSpan.Zero);
    private static readonly TimeSpan MaxFutureSkew = TimeSpan.FromMinutes(5);

    private static readonly EventMessage ValidMessage = new(
        "EVT-10001", "ATS", "route-service", "CRITICAL", "Route locking failed", "OPEN", "2026-06-01T10:15:00Z");

    private static ValidationResult Validate(EventMessage message) => EventValidator.Validate(message, Now, MaxFutureSkew);

    [Fact]
    public void Valid_message_becomes_an_event()
    {
        var result = Validate(ValidMessage);

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
        var result = Validate(new EventMessage(null, null, null, null, null, null, null));

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
        var result = Validate(ValidMessage with { Severity = severity, Status = status });

        Assert.False(result.IsValid);
    }

    // --- EventId -----------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("EVT-10001")]
    [InlineData("EVT-550E8400E29B41D4A716446655440000")]
    [InlineData("scada_alarm.42")]
    public void Event_id_with_letters_digits_hyphen_underscore_and_dot_is_accepted(string eventId)
    {
        Assert.True(Validate(ValidMessage with { EventId = eventId }).IsValid);
    }

    [Theory]
    [InlineData("EVT/1")]
    [InlineData(@"EVT\1")]
    [InlineData("EVT?1")]
    [InlineData("EVT#1")]
    [InlineData("EVT%201")]
    [InlineData("EVT 1")]
    [InlineData("EVT-É1")]
    public void Event_id_with_other_characters_is_rejected(string eventId)
    {
        var result = Validate(ValidMessage with { EventId = eventId });

        Assert.Contains(result.Errors, e => e.StartsWith("eventId may only contain"));
    }

    [Fact]
    public void Event_id_longer_than_64_characters_is_rejected()
    {
        var result = Validate(ValidMessage with { EventId = new string('A', 65) });

        Assert.Contains(result.Errors, e => e.StartsWith("eventId must be at most"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_event_id_is_rejected(string? eventId)
    {
        var result = Validate(ValidMessage with { EventId = eventId });

        Assert.Equal(["eventId is required"], result.Errors);
    }

    [Fact]
    public void Surrounding_whitespace_is_trimmed_like_the_other_text_fields()
    {
        var result = Validate(ValidMessage with { EventId = "  EVT-10001  ", Source = " ATS " });

        Assert.True(result.IsValid);
        Assert.Equal("EVT-10001", result.Event!.EventId);
        Assert.Equal("ATS", result.Event.Source);
    }

    // --- Timestamp format --------------------------------------------------------------------------------------

    [Theory]
    [InlineData("2026-09-28T14:35:12Z")]
    [InlineData("2026-09-28T14:35:12.1Z")]
    [InlineData("2026-09-28T14:35:12.123Z")]
    [InlineData("2026-09-28T14:35:12.123456Z")]
    [InlineData("2026-09-28T14:35:12.1234567Z")]
    [InlineData("2026-09-28T17:35:12+03:00")]
    [InlineData("2026-09-28T17:35:12.123+03:00")]
    public void Iso_timestamp_with_time_zone_is_accepted(string timestamp)
    {
        Assert.True(Validate(ValidMessage with { Timestamp = timestamp }).IsValid);
    }

    [Theory]
    [InlineData("2026-09-28T14:35:12")]
    [InlineData("2026-09-28 14:35:12")]
    [InlineData("2026-09-28 14:35:12Z")]
    [InlineData("09/28/2026 14:35:12")]
    [InlineData("28.09.2026 14:35:12")]
    [InlineData("2026-09-28T14:35:12.12345678Z")]
    [InlineData("2026-09-28T25:35:12Z")]
    [InlineData("yesterday")]
    public void Timestamp_that_is_not_iso_with_time_zone_is_rejected(string timestamp)
    {
        var result = Validate(ValidMessage with { Timestamp = timestamp });

        Assert.Contains(result.Errors, e => e.StartsWith("timestamp must be ISO-8601"));
    }

    [Fact]
    public void Offset_timestamp_is_stored_as_the_same_instant_in_utc()
    {
        var result = Validate(ValidMessage with { Timestamp = "2026-09-28T17:35:12.123+03:00" });

        Assert.Equal(new DateTime(2026, 9, 28, 14, 35, 12, 123, DateTimeKind.Utc), result.Event!.Timestamp);
        Assert.Equal(DateTimeKind.Utc, result.Event.Timestamp.Kind);
    }

    [Fact]
    public void Seven_fractional_digits_are_truncated_to_microseconds()
    {
        var result = Validate(ValidMessage with { Timestamp = "2026-09-28T14:35:12.1234567Z" });

        var expected = new DateTime(2026, 9, 28, 14, 35, 12, DateTimeKind.Utc).AddTicks(1_234_560);
        Assert.Equal(expected, result.Event!.Timestamp);
    }

    // --- Future timestamps -------------------------------------------------------------------------------------

    [Fact]
    public void Timestamp_exactly_at_the_allowed_skew_is_accepted()
    {
        var atLimit = (Now + MaxFutureSkew).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

        Assert.True(Validate(ValidMessage with { Timestamp = atLimit }).IsValid);
    }

    [Fact]
    public void Timestamp_beyond_the_allowed_skew_is_rejected()
    {
        var tooFar = (Now + MaxFutureSkew).AddMilliseconds(1).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

        var result = Validate(ValidMessage with { Timestamp = tooFar });

        Assert.Contains(result.Errors, e => e.StartsWith("timestamp must not be more than"));
    }

    [Fact]
    public void Old_event_is_accepted()
    {
        var oneYearAgo = Now.AddYears(-1).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

        Assert.True(Validate(ValidMessage with { Timestamp = oneYearAgo }).IsValid);
    }

    // --- ReceivedAt ----------------------------------------------------------------------------------------------

    [Fact]
    public void Received_at_is_the_supplied_time_in_utc_truncated_to_microseconds()
    {
        var nowWithTicks = new DateTimeOffset(2026, 9, 28, 17, 35, 12, TimeSpan.FromHours(3)).AddTicks(1_234_567);

        var result = EventValidator.Validate(ValidMessage, nowWithTicks, MaxFutureSkew);

        var expected = new DateTime(2026, 9, 28, 14, 35, 12, DateTimeKind.Utc).AddTicks(1_234_560);
        Assert.Equal(expected, result.Event!.ReceivedAt);
        Assert.Equal(DateTimeKind.Utc, result.Event.ReceivedAt.Kind);
    }
}
