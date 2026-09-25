using IncidentMonitoring.Core.Dtos;
using IncidentMonitoring.Core.Interfaces;
using IncidentMonitoring.Core.Models;
using IncidentMonitoring.Core.Rules;

namespace IncidentMonitoring.Core.Services;

/// <summary>Builds the dashboard and service status data from the live state in Redis.</summary>
public class DashboardService(IDashboardStore dashboardStore)
{
    public async Task<DashboardSummaryDto> GetSummaryAsync()
    {
        var counters = await dashboardStore.GetCountersAsync();
        var services = await GetServicesAsync();

        return new DashboardSummaryDto(
            TotalEvents: counters.Total,
            OpenEvents: counters.ByStatus[EventStatus.OPEN],
            CriticalEvents: counters.BySeverity[Severity.CRITICAL],
            SeverityDistribution: counters.BySeverity,
            StatusDistribution: counters.ByStatus,
            Services: services);
    }

    public async Task<List<ServiceStatusDto>> GetServicesAsync()
    {
        var services = await dashboardStore.GetServicesAsync();

        return services
            .Select(s => new ServiceStatusDto(
                Name: s.Name,
                Status: ServiceHealthRule.Evaluate(s.OpenBySeverity),
                LastEventTime: s.LastEventTime,
                LatestSeverity: s.LatestSeverity,
                OpenIncidentCount: s.OpenBySeverity.Values.Sum()))
            .ToList();
    }
}
