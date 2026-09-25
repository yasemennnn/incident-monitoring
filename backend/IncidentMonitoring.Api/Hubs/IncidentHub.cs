using Microsoft.AspNetCore.SignalR;

namespace IncidentMonitoring.Api.Hubs;

/// <summary>
/// SignalR hub at /hubs/incidents. The server only pushes; clients listen for:
///   eventReceived  (EventDto)            a new event was consumed from Kafka
///   eventUpdated   (EventDto)            an event's status was changed
///   summaryUpdated (DashboardSummaryDto) the dashboard numbers changed
/// </summary>
public class IncidentHub : Hub;
