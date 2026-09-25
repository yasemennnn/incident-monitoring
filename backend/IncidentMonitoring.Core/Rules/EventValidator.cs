using System.Globalization;
using IncidentMonitoring.Core.Dtos;
using IncidentMonitoring.Core.Models;

namespace IncidentMonitoring.Core.Rules;

public record ValidationResult(IncidentEvent? Event, List<string> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

/// <summary>Checks an incoming Kafka message and converts it into an <see cref="IncidentEvent"/>.</summary>
public static class EventValidator
{
    // Same limits as the database columns.
    public const int MaxIdLength = 64;
    public const int MaxNameLength = 100;
    public const int MaxMessageLength = 1000;

    public static ValidationResult Validate(EventMessage message)
    {
        var errors = new List<string>();

        CheckText(message.EventId, "eventId", MaxIdLength, errors);
        CheckText(message.Source, "source", MaxNameLength, errors);
        CheckText(message.Service, "service", MaxNameLength, errors);
        CheckText(message.Message, "message", MaxMessageLength, errors);

        if (!TryParseEnum(message.Severity, out Severity severity))
            errors.Add("severity must be one of INFO, WARNING, MAJOR, CRITICAL");

        if (!TryParseEnum(message.Status, out EventStatus status))
            errors.Add("status must be one of OPEN, ACKNOWLEDGED, RESOLVED");

        if (!DateTime.TryParse(message.Timestamp, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var timestamp))
            errors.Add("timestamp must be an ISO-8601 date, e.g. 2026-06-01T10:15:00Z");

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
            Timestamp = timestamp,
            ReceivedAt = DateTime.UtcNow
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

    /// <summary>Accepts the enum names (case-insensitive) but not numbers such as "2".</summary>
    private static bool TryParseEnum<T>(string? value, out T result) where T : struct, Enum
    {
        result = default;
        return !string.IsNullOrWhiteSpace(value)
               && value.All(char.IsLetter)
               && Enum.TryParse(value, ignoreCase: true, out result);
    }
}
