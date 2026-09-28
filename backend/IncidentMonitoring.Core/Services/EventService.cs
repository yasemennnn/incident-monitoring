using IncidentMonitoring.Core.Dtos;
using IncidentMonitoring.Core.Interfaces;
using IncidentMonitoring.Core.Models;
using IncidentMonitoring.Core.Rules;
using Microsoft.Extensions.Logging;

namespace IncidentMonitoring.Core.Services;

/// <summary>Reads events and changes their status (used by the REST API).</summary>
public class EventService(
    IEventRepository repository,
    IDashboardRefresher dashboardRefresher,
    IEventNotifier notifier,
    TimeProvider timeProvider,
    ILogger<EventService> logger)
{
    public async Task<PagedResult<EventDto>> GetEventsAsync(EventFilter filter)
    {
        var page = await repository.SearchAsync(filter);
        return new PagedResult<EventDto>(page.Items.Select(EventDto.FromEntity).ToList(), page.Page, page.PageSize, page.TotalCount);
    }

    public async Task<EventDto> GetEventAsync(string eventId)
    {
        var incidentEvent = await repository.GetAsync(eventId) ?? throw new NotFoundException($"Event '{eventId}' was not found.");
        return EventDto.FromEntity(incidentEvent);
    }

    /// <summary>The latest events by event time, newest first (from PostgreSQL).</summary>
    public async Task<List<EventDto>> GetRecentEventsAsync(int count) =>
        (await repository.GetRecentAsync(count)).Select(EventDto.FromEntity).ToList();

    public Task<EventFacetsDto> GetFacetsAsync() => repository.GetFacetsAsync();

    public async Task<EventDto> UpdateStatusAsync(string eventId, EventStatus newStatus)
    {
        var incidentEvent = await repository.GetAsync(eventId) ?? throw new NotFoundException($"Event '{eventId}' was not found.");

        // Asking for the current status is not a transition: nothing changes, so repeating a request is harmless.
        if (incidentEvent.Status == newStatus)
            return EventDto.FromEntity(incidentEvent);

        var oldStatus = incidentEvent.Status;
        if (!StatusRules.CanChange(oldStatus, newStatus))
            throw new InvalidStatusTransitionException($"Cannot change status from {oldStatus} to {newStatus}.");

        // 1. PostgreSQL: the update only applies if the status is still the one validated above.
        var statusUpdatedAt = TimePrecision.ToMicroseconds(timeProvider.GetUtcNow().UtcDateTime);
        if (!await repository.TryUpdateStatusAsync(eventId, oldStatus, newStatus, statusUpdatedAt))
            return await ResolveLostUpdateAsync(eventId, oldStatus, newStatus);

        // This request won. The event read above is not tracked, so changing it only affects the response.
        incidentEvent.Status = newStatus;
        incidentEvent.StatusUpdatedAt = statusUpdatedAt;

        // 2. The dashboard is rebuilt from PostgreSQL in the background; this only sets a flag, so a Redis outage
        //    cannot fail a status change that is already stored.
        dashboardRefresher.RequestRefresh();

        logger.LogInformation("Event {EventId} status changed from {OldStatus} to {NewStatus}", eventId, oldStatus, newStatus);

        // 3. SignalR
        var dto = EventDto.FromEntity(incidentEvent);
        await notifier.EventUpdatedAsync(dto);
        return dto;
    }

    /// <summary>
    /// The conditional update matched no row, so the event was deleted or another request changed its status first.
    /// The update is not retried from the new status: that would be a different transition than the one validated.
    /// </summary>
    private async Task<EventDto> ResolveLostUpdateAsync(string eventId, EventStatus expectedStatus, EventStatus newStatus)
    {
        var current = await repository.GetAsync(eventId) ?? throw new NotFoundException($"Event '{eventId}' was not found.");

        // Another request already made the same change: the requested state is in place.
        if (current.Status == newStatus)
            return EventDto.FromEntity(current);

        throw new InvalidStatusTransitionException(
            $"The status was changed concurrently from {expectedStatus} to {current.Status}; the change to {newStatus} was not applied.");
    }
}
