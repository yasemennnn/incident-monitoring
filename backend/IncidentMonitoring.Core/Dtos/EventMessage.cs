namespace IncidentMonitoring.Core.Dtos;

/// <summary>
/// The JSON message published to Kafka. All fields are strings because the content is untrusted;
/// <see cref="Rules.EventValidator"/> checks it before it becomes an <see cref="Models.IncidentEvent"/>.
/// </summary>
public record EventMessage(
    string? EventId,
    string? Source,
    string? Service,
    string? Severity,
    string? Message,
    string? Status,
    string? Timestamp);
