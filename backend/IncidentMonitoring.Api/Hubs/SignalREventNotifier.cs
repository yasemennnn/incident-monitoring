using IncidentMonitoring.Core.Dtos;
using IncidentMonitoring.Core.Interfaces;
using Microsoft.AspNetCore.SignalR;

namespace IncidentMonitoring.Api.Hubs;

/// <summary>
/// Pushes changes to all connected clients. Each method sends exactly one message and reads nothing: the dashboard
/// summary is pushed by the projection worker after it has written the projection to Redis.
/// </summary>
public class SignalREventNotifier(IHubContext<IncidentHub> hub, ILogger<SignalREventNotifier> logger) : IEventNotifier
{
    public Task EventReceivedAsync(EventDto incidentEvent) => SendAsync("eventReceived", incidentEvent, incidentEvent.EventId);

    public Task EventUpdatedAsync(EventDto incidentEvent) => SendAsync("eventUpdated", incidentEvent, incidentEvent.EventId);

    public Task SummaryUpdatedAsync(DashboardSummaryDto summary) => SendAsync("summaryUpdated", summary, "dashboard summary");

    private async Task SendAsync(string method, object payload, string subject)
    {
        // A failed notification must never fail event processing, a status update or a projection rebuild.
        try
        {
            await hub.Clients.All.SendAsync(method, payload);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "SignalR notification {Method} failed for {Subject}", method, subject);
        }
    }
}
