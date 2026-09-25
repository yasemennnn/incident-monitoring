namespace IncidentMonitoring.Core.Models;

/// <summary>An event received from Kafka and stored in PostgreSQL.</summary>
public class IncidentEvent
{
    public string EventId { get; set; } = "";
    public string Source { get; set; } = "";
    public string Service { get; set; } = "";
    public Severity Severity { get; set; }
    public string Message { get; set; } = "";
    public EventStatus Status { get; set; }

    /// <summary>When the event happened (UTC), taken from the message.</summary>
    public DateTime Timestamp { get; set; }

    /// <summary>When this system consumed the event (UTC).</summary>
    public DateTime ReceivedAt { get; set; }

    /// <summary>When the status was last changed through the API (UTC).</summary>
    public DateTime? StatusUpdatedAt { get; set; }
}
