namespace IncidentMonitoring.Infrastructure.Kafka;

/// <summary>Bound from the "Kafka" section of appsettings.json or Kafka__* environment variables.</summary>
public class KafkaSettings
{
    public string BootstrapServers { get; set; } = "localhost:29092";
    public string Topic { get; set; } = "incident-events";
    public string DeadLetterTopic { get; set; } = "incident-events-dlq";

    /// <summary>All API instances share this consumer group, so each message is processed once.</summary>
    public string GroupId { get; set; } = "incident-monitoring-api";

    /// <summary>
    /// How many times a message is tried (including the first) before it goes to the dead-letter topic.
    /// Applies only to failures that are not a temporary PostgreSQL outage; those are retried without limit.
    /// </summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>First retry delay; each further retry waits twice as long (1 s, 2 s, 4 s, ...), up to MaxRetryDelayMs.</summary>
    public int RetryDelayMs { get; set; } = 1000;

    /// <summary>Longest wait between two retries during a PostgreSQL outage.</summary>
    public int MaxRetryDelayMs { get; set; } = 30000;
}
