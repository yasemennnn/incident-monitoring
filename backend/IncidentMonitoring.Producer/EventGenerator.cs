namespace IncidentMonitoring.Producer;

/// <summary>The event schema from the assignment.</summary>
public record EventMessage(
    string EventId,
    string Source,
    string Service,
    string Severity,
    string Message,
    string Status,
    string Timestamp);

/// <summary>Creates random but realistic events.</summary>
public static class EventGenerator
{
    // Each source system owns a few services.
    private static readonly Dictionary<string, string[]> ServicesBySource = new()
    {
        ["ATS"] = ["route-service", "timetable-service", "train-tracking-service"],
        ["SCADA"] = ["power-supply-service", "ventilation-service"],
        ["CBTC"] = ["zone-controller-service", "onboard-comm-service"],
        ["PIS"] = ["passenger-info-service"]
    };

    private static readonly Dictionary<string, string[]> MessagesBySeverity = new()
    {
        ["INFO"] = ["Heartbeat received", "Configuration reloaded", "Train arrived at platform"],
        ["WARNING"] = ["Response time above threshold", "Message queue backlog growing", "Disk usage above 85%"],
        ["MAJOR"] = ["Communication timeout with wayside unit", "Failover to standby node", "Train position report delayed"],
        ["CRITICAL"] = ["Route locking failed", "Loss of communication with zone controller", "Power supply failure on section"]
    };

    // Repeating a value makes it more likely: most events are INFO and OPEN.
    private static readonly string[] Severities = ["INFO", "INFO", "INFO", "INFO", "WARNING", "WARNING", "MAJOR", "CRITICAL"];
    private static readonly string[] Statuses = ["OPEN", "OPEN", "OPEN", "OPEN", "ACKNOWLEDGED", "RESOLVED"];

    public static EventMessage Create()
    {
        var source = Pick(ServicesBySource.Keys.ToArray());
        var severity = Pick(Severities);

        return new EventMessage(
            EventId: $"EVT-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}",
            Source: source,
            Service: Pick(ServicesBySource[source]),
            Severity: severity,
            Message: Pick(MessagesBySeverity[severity]),
            Status: Pick(Statuses),
            Timestamp: DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"));
    }

    private static string Pick(string[] values) => values[Random.Shared.Next(values.Length)];
}
