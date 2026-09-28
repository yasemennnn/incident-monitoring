using IncidentMonitoring.Core.Models;

namespace IncidentMonitoring.Core.Rules;

/// <summary>
/// Builds the complete dashboard from a PostgreSQL snapshot. Every value is recomputed from the snapshot,
/// so building the same snapshot twice always gives the same dashboard.
/// </summary>
public static class DashboardProjection
{
    public static DashboardState Build(DashboardSnapshot snapshot)
    {
        var bySeverity = Enum.GetValues<Severity>().ToDictionary(s => s, _ => 0L);
        var byStatus = Enum.GetValues<EventStatus>().ToDictionary(s => s, _ => 0L);
        long total = 0;
        long critical = 0;
        var openByService = new Dictionary<string, Dictionary<Severity, long>>();

        foreach (var row in snapshot.Counts)
        {
            total += row.Count;
            bySeverity[row.Severity] += row.Count;
            byStatus[row.Status] += row.Count;

            // A service is listed as soon as it has any event, even if all of them are resolved.
            if (!openByService.TryGetValue(row.Service, out var open))
                openByService[row.Service] = open = Enum.GetValues<Severity>().ToDictionary(s => s, _ => 0L);

            if (StatusRules.IsOpen(row.Status))
            {
                open[row.Severity] += row.Count;
                if (row.Severity == Severity.CRITICAL)
                    critical += row.Count;
            }
        }

        // The repository supplies the latest event per service; it is not derived from the counts.
        var latest = snapshot.Latest.ToDictionary(l => l.Service);

        var services = openByService
            .OrderBy(s => s.Key, StringComparer.Ordinal)
            .Select(s => latest.TryGetValue(s.Key, out var last)
                ? new ServiceState(s.Key, last.Timestamp, last.Severity, s.Value)
                : new ServiceState(s.Key, null, null, s.Value))
            .ToList();

        return new DashboardState(new DashboardCounters(total, bySeverity, byStatus), critical, services, snapshot.SnapshotAt);
    }
}
