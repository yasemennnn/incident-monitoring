namespace IncidentMonitoring.Core.Rules;

/// <summary>Bound from the "EventValidation" section of appsettings.json or EventValidation__* environment variables.</summary>
public class EventValidationOptions
{
    /// <summary>
    /// How far an event timestamp may be ahead of this server's clock. A wrong future timestamp would otherwise
    /// stay the "latest" event of its service indefinitely.
    /// </summary>
    public TimeSpan MaxFutureSkew { get; set; } = TimeSpan.FromMinutes(5);
}
