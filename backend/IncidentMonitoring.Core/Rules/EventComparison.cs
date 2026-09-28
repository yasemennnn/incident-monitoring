using IncidentMonitoring.Core.Models;

namespace IncidentMonitoring.Core.Rules;

/// <summary>
/// Compares the fields of an event that never change after it is received.
/// Status and StatusUpdatedAt are left out because users change them through the API;
/// ReceivedAt is left out because this system sets it on every delivery.
/// </summary>
public static class EventComparison
{
    /// <returns>Names of the immutable fields that differ, always in the same order; empty when they are all equal.</returns>
    public static List<string> DifferingImmutableFields(IncidentEvent stored, IncidentEvent incoming)
    {
        var differences = new List<string>();

        if (stored.Source != incoming.Source)
            differences.Add("source");
        if (stored.Service != incoming.Service)
            differences.Add("service");
        if (stored.Severity != incoming.Severity)
            differences.Add("severity");
        if (stored.Message != incoming.Message)
            differences.Add("message");
        // PostgreSQL keeps microseconds, so a difference below that cannot come from the stored event.
        if (TimePrecision.ToMicroseconds(stored.Timestamp) != TimePrecision.ToMicroseconds(incoming.Timestamp))
            differences.Add("timestamp");

        return differences;
    }
}
