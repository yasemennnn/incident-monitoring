namespace IncidentMonitoring.Producer;

/// <summary>Bound from the "Producer" section of appsettings.json or Producer__* environment variables.</summary>
public class ProducerSettings
{
    public string BootstrapServers { get; set; } = "localhost:29092";
    public string Topic { get; set; } = "incident-events";

    /// <summary>Delay between two events.</summary>
    public int IntervalMs { get; set; } = 2000;

    /// <summary>0 = run until stopped. A positive number sends that many events and exits (manual run).</summary>
    public int Count { get; set; } = 0;
}
