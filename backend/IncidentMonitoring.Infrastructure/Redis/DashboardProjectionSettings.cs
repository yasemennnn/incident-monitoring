namespace IncidentMonitoring.Infrastructure.Redis;

/// <summary>Bound from the "DashboardProjection" section of appsettings.json or DashboardProjection__* environment variables.</summary>
public class DashboardProjectionSettings
{
    /// <summary>
    /// The dashboard is rebuilt at least this often even when nothing asks for it, so any difference between
    /// Redis and PostgreSQL is corrected within this time.
    /// </summary>
    public int RefreshIntervalSeconds { get; set; } = 30;
}
