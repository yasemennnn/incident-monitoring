using IncidentMonitoring.Core.Dtos;
using IncidentMonitoring.Core.Interfaces;
using IncidentMonitoring.Core.Services;
using Microsoft.AspNetCore.SignalR;

namespace IncidentMonitoring.Api.Hubs;

/// <summary>Sends the changed event and the new dashboard summary to all connected clients.</summary>
public class SignalREventNotifier(
    IHubContext<IncidentHub> hub,
    DashboardService dashboardService,
    ILogger<SignalREventNotifier> logger) : IEventNotifier
{
    public Task EventReceivedAsync(EventDto incidentEvent) => SendAsync("eventReceived", incidentEvent);

    public Task EventUpdatedAsync(EventDto incidentEvent) => SendAsync("eventUpdated", incidentEvent);

    private async Task SendAsync(string method, EventDto incidentEvent)
    {
        // A failed notification must never fail event processing or the status update.
        try
        {
            await hub.Clients.All.SendAsync(method, incidentEvent);
            await hub.Clients.All.SendAsync("summaryUpdated", await dashboardService.GetSummaryAsync());
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "SignalR notification {Method} failed for {EventId}", method, incidentEvent.EventId);
        }
    }
}
