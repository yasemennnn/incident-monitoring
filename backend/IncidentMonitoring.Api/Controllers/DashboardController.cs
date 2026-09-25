using IncidentMonitoring.Core.Dtos;
using IncidentMonitoring.Core.Services;
using Microsoft.AspNetCore.Mvc;

namespace IncidentMonitoring.Api.Controllers;

[ApiController]
[Produces("application/json")]
public class DashboardController(DashboardService dashboardService) : ControllerBase
{
    /// <summary>Totals, severity and status distribution and service statuses (from Redis).</summary>
    [HttpGet("api/dashboard/summary")]
    [ProducesResponseType<DashboardSummaryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<DashboardSummaryDto> GetSummary() =>
        await dashboardService.GetSummaryAsync();

    /// <summary>Current status per service: health, last event time, latest severity, open incident count (from Redis).</summary>
    [HttpGet("api/services")]
    [ProducesResponseType<List<ServiceStatusDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<List<ServiceStatusDto>> GetServices() =>
        await dashboardService.GetServicesAsync();
}
