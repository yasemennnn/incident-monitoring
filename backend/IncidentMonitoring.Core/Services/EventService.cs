using IncidentMonitoring.Core.Dtos;
using IncidentMonitoring.Core.Interfaces;
using IncidentMonitoring.Core.Models;
using IncidentMonitoring.Core.Rules;
using Microsoft.Extensions.Logging;

namespace IncidentMonitoring.Core.Services;

/// <summary>Reads events and changes their status (used by the REST API).</summary>
public class EventService(
    IEventRepository repository,
    IDashboardStore dashboardStore,
    IEventNotifier notifier,
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

    /// <summary>The latest events: ids come from the Redis list, details from PostgreSQL.</summary>
    public async Task<List<EventDto>> GetRecentEventsAsync(int count)
    {
        var ids = await dashboardStore.GetRecentEventIdsAsync(count);
        var events = (await repository.GetByIdsAsync(ids)).ToDictionary(e => e.EventId);

        // Keep the newest-first order of the Redis list.
        return ids.Where(events.ContainsKey).Select(id => EventDto.FromEntity(events[id])).ToList();
    }

    public Task<EventFacetsDto> GetFacetsAsync() => repository.GetFacetsAsync();

    public async Task<EventDto> UpdateStatusAsync(string eventId, EventStatus newStatus)
    {
        var incidentEvent = await repository.GetAsync(eventId) ?? throw new NotFoundException($"Event '{eventId}' was not found.");

        var oldStatus = incidentEvent.Status;
        if (!StatusRules.CanChange(oldStatus, newStatus))
            throw new InvalidStatusTransitionException($"Cannot change status from {oldStatus} to {newStatus}.");

        // 1. PostgreSQL
        incidentEvent.Status = newStatus;
        incidentEvent.StatusUpdatedAt = DateTime.UtcNow;
        await repository.SaveChangesAsync();

        // 2. Redis
        try
        {
            await dashboardStore.UpdateStatusAsync(incidentEvent, oldStatus);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Redis update failed for status change of {EventId}; dashboard counters are out of date", eventId);
        }

        logger.LogInformation("Event {EventId} status changed from {OldStatus} to {NewStatus}", eventId, oldStatus, newStatus);

        // 3. SignalR
        var dto = EventDto.FromEntity(incidentEvent);
        await notifier.EventUpdatedAsync(dto);
        return dto;
    }
}
