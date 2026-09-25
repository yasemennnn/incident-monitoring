namespace IncidentMonitoring.Core.Models;

// Enum member names are UPPERCASE on purpose: they are exactly the values of the event schema,
// so the same text is used in Kafka messages, the REST API, SignalR, Redis keys and the database.

public enum Severity
{
    INFO,
    WARNING,
    MAJOR,
    CRITICAL
}

public enum EventStatus
{
    OPEN,
    ACKNOWLEDGED,
    RESOLVED
}

public enum ServiceHealth
{
    HEALTHY,
    WARNING,
    DEGRADED,
    DOWN
}
