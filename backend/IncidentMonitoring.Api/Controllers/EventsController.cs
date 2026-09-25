using IncidentMonitoring.Core.Dtos;
using IncidentMonitoring.Core.Services;
using Microsoft.AspNetCore.Mvc;

namespace IncidentMonitoring.Api.Controllers;

[ApiController]
[Route("api/events")]
[Produces("application/json")]
public class EventsController(EventService eventService) : ControllerBase
{
    /// <summary>Lists events, newest first, with optional filters, search and paging.</summary>
    /// <remarks>Example: GET /api/events?severity=CRITICAL&amp;status=OPEN&amp;source=ATS&amp;search=route&amp;page=1&amp;pageSize=20</remarks>
    [HttpGet]
    [ProducesResponseType<PagedResult<EventDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<PagedResult<EventDto>> GetEvents([FromQuery] EventFilter filter) =>
        await eventService.GetEventsAsync(filter);

    /// <summary>The most recently received events, newest first (from the Redis recent list).</summary>
    /// <param name="count">Number of events, 1-50 (default 10).</param>
    [HttpGet("recent")]
    [ProducesResponseType<List<EventDto>>(StatusCodes.Status200OK)]
    public async Task<List<EventDto>> GetRecentEvents([FromQuery] int count = 10) =>
        await eventService.GetRecentEventsAsync(Math.Clamp(count, 1, 50));

    /// <summary>Distinct sources and services plus all severities and statuses, for filter drop-downs.</summary>
    [HttpGet("facets")]
    [ProducesResponseType<EventFacetsDto>(StatusCodes.Status200OK)]
    public async Task<EventFacetsDto> GetFacets() =>
        await eventService.GetFacetsAsync();

    /// <summary>Returns one event.</summary>
    [HttpGet("{eventId}")]
    [ProducesResponseType<EventDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<EventDto> GetEvent(string eventId) =>
        await eventService.GetEventAsync(eventId);

    /// <summary>Changes the status of an event.</summary>
    /// <remarks>
    /// Allowed changes: OPEN → ACKNOWLEDGED or RESOLVED, ACKNOWLEDGED → RESOLVED, RESOLVED → OPEN.
    /// Any other change returns 409.
    /// </remarks>
    [HttpPut("{eventId}/status")]
    [ProducesResponseType<EventDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<EventDto> UpdateStatus(string eventId, [FromBody] UpdateStatusRequest request) =>
        await eventService.UpdateStatusAsync(eventId, request.Status!.Value);
}
