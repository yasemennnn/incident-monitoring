using Microsoft.AspNetCore.SignalR;

namespace IncidentMonitoring.Api.Hubs;

/// <summary>
/// SignalR hub at /hubs/incidents. The server only pushes; clients listen for:
///   eventReceived  (EventDto)            a new event was consumed from Kafka and stored
///   eventUpdated   (EventDto)            an event's status was changed
///   summaryUpdated (DashboardSummaryDto) the dashboard projection was rebuilt from PostgreSQL
/// summaryUpdated is sent by the projection worker, so it can arrive before or after the event message that caused it.
/// </summary>
public class IncidentHub : Hub;
