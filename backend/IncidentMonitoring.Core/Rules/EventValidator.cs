using System.Globalization;
using IncidentMonitoring.Core.Dtos;
using IncidentMonitoring.Core.Models;

namespace IncidentMonitoring.Core.Rules;

public record ValidationResult(IncidentEvent? Event, List<string> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

/// <summary>
/// Checks an incoming Kafka message and converts it into an <see cref="IncidentEvent"/>.
/// Text fields are trimmed before they are checked and stored.
/// </summary>
public static class EventValidator
{
    // Same limits as the database columns.
    public const int MaxIdLength = 64;
    public const int MaxNameLength = 100;
    public const int MaxMessageLength = 1000;

    // ISO-8601 with 0 to 7 fractional second digits and a mandatory time zone: "Z" or an offset such as "+03:00".
    private static readonly string[] TimestampFormats =
        Enumerable.Range(0, 8)
            .Select(digits => "yyyy-MM-dd'T'HH:mm:ss" + (digits == 0 ? "" : "." + new string('f', digits)))
            .SelectMany(format => new[] { format + "'Z'", format + "zzz" })
            .ToArray();

    /// <param name="now">The consumer's current time, used for the future-timestamp check and ReceivedAt.</param>
    public static ValidationResult Validate(EventMessage message, DateTimeOffset now, TimeSpan maxFutureSkew)
    {
        var errors = new List<string>();

        CheckText(message.EventId, "eventId", MaxIdLength, errors);
        CheckEventIdCharacters(message.EventId, errors);
        CheckText(message.Source, "source", MaxNameLength, errors);
        CheckText(message.Service, "service", MaxNameLength, errors);
        CheckText(message.Message, "message", MaxMessageLength, errors);

        if (!TryParseEnum(message.Severity, out Severity severity))
            errors.Add("severity must be one of INFO, WARNING, MAJOR, CRITICAL");

        if (!TryParseEnum(message.Status, out EventStatus status))
            errors.Add("status must be one of OPEN, ACKNOWLEDGED, RESOLVED");

        if (!TryParseTimestamp(message.Timestamp, out var timestamp))
            errors.Add("timestamp must be ISO-8601 with a time zone, e.g. 2026-06-01T10:15:00.123Z");
        // Late events are fine; only timestamps too far in the future are rejected, never clamped.
        else if (timestamp > now + maxFutureSkew)
            errors.Add($"timestamp must not be more than {maxFutureSkew} in the future");

        if (errors.Count > 0)
            return new ValidationResult(null, errors);

        var incidentEvent = new IncidentEvent
        {
            EventId = message.EventId!.Trim(),
            Source = message.Source!.Trim(),
            Service = message.Service!.Trim(),
            Severity = severity,
            Message = message.Message!.Trim(),
            Status = status,
            Timestamp = TimePrecision.ToMicroseconds(timestamp.UtcDateTime),
            ReceivedAt = TimePrecision.ToMicroseconds(now.UtcDateTime)
        };
        return new ValidationResult(incidentEvent, errors);
    }

    private static void CheckText(string? value, string field, int maxLength, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
            errors.Add($"{field} is required");
        else if (value.Trim().Length > maxLength)
            errors.Add($"{field} must be at most {maxLength} characters");
    }

    /// <summary>
    /// The event id is used in URLs such as /api/events/{eventId}/status, so characters like '/', '?', '#',
    /// '%' or spaces would make the event unreachable.
    /// </summary>
    private static void CheckEventIdCharacters(string? eventId, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(eventId))
            return; // already reported as required

        if (!eventId.Trim().All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'))
            errors.Add("eventId may only contain letters, digits, '-', '_' and '.'");
    }

    private static bool TryParseTimestamp(string? value, out DateTimeOffset timestamp)
    {
        timestamp = default;
        // Every format ends in a literal 'Z' or a ±HH:mm offset, so input without a time zone never matches.
        // AssumeUniversal is what makes the literal 'Z' mean UTC; without it 'Z' would be read as local time.
        return !string.IsNullOrWhiteSpace(value)
               && DateTimeOffset.TryParseExact(value.Trim(), TimestampFormats, CultureInfo.InvariantCulture,
                   DateTimeStyles.AssumeUniversal, out timestamp);
    }

    /// <summary>Accepts the enum names (case-insensitive) but not numbers such as "2".</summary>
    private static bool TryParseEnum<T>(string? value, out T result) where T : struct, Enum
    {
        result = default;
        return !string.IsNullOrWhiteSpace(value)
               && value.All(char.IsLetter)
               && Enum.TryParse(value, ignoreCase: true, out result);
    }
}
