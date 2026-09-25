using IncidentMonitoring.Core.Models;

namespace IncidentMonitoring.Core.Rules;

/// <summary>
/// Allowed status changes:
///   OPEN         -> ACKNOWLEDGED or RESOLVED
///   ACKNOWLEDGED -> RESOLVED
///   RESOLVED     -> OPEN (reopen)
/// </summary>
public static class StatusRules
{
    public static bool CanChange(EventStatus from, EventStatus to) => (from, to) switch
    {
        (EventStatus.OPEN, EventStatus.ACKNOWLEDGED) => true,
        (EventStatus.OPEN, EventStatus.RESOLVED) => true,
        (EventStatus.ACKNOWLEDGED, EventStatus.RESOLVED) => true,
        (EventStatus.RESOLVED, EventStatus.OPEN) => true,
        _ => false
    };

    /// <summary>An incident is "open" until it is resolved (so ACKNOWLEDGED still counts as open).</summary>
    public static bool IsOpen(EventStatus status) => status != EventStatus.RESOLVED;
}
