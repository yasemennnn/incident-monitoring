using IncidentMonitoring.Core.Dtos;
using IncidentMonitoring.Core.Interfaces;

namespace IncidentMonitoring.Core.Services;

/// <summary>Reads the dashboard and service status data from the projection in Redis.</summary>
public class DashboardService(IDashboardStore dashboardStore)
{
    public async Task<DashboardSummaryDto> GetSummaryAsync()
    {
        var totals = await dashboardStore.GetCountersAsync();
        var services = await dashboardStore.GetServicesAsync();

        return DashboardSummaryDto.From(totals.Counters, totals.CriticalEvents, totals.SnapshotAt, services);
    }

    public async Task<List<ServiceStatusDto>> GetServicesAsync() =>
        (await dashboardStore.GetServicesAsync()).Select(ServiceStatusDto.From).ToList();
}
