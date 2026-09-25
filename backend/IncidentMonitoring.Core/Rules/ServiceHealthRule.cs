using IncidentMonitoring.Core.Models;

namespace IncidentMonitoring.Core.Rules;

/// <summary>
/// A service's health is decided by its worst open (not RESOLVED) incident:
///   CRITICAL -> DOWN, MAJOR -> DEGRADED, WARNING -> WARNING, otherwise HEALTHY.
/// </summary>
public static class ServiceHealthRule
{
    public static ServiceHealth Evaluate(Dictionary<Severity, long> openBySeverity)
    {
        if (openBySeverity.GetValueOrDefault(Severity.CRITICAL) > 0) return ServiceHealth.DOWN;
        if (openBySeverity.GetValueOrDefault(Severity.MAJOR) > 0) return ServiceHealth.DEGRADED;
        if (openBySeverity.GetValueOrDefault(Severity.WARNING) > 0) return ServiceHealth.WARNING;
        return ServiceHealth.HEALTHY;
    }
}
