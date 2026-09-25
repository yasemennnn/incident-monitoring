using System.ComponentModel.DataAnnotations;
using IncidentMonitoring.Core.Models;

namespace IncidentMonitoring.Core.Dtos;

/// <summary>An event as returned by the API and pushed over SignalR.</summary>
public record EventDto(
    string EventId,
    string Source,
    string Service,
    Severity Severity,
    string Message,
    EventStatus Status,
    DateTime Timestamp,
    DateTime ReceivedAt,
    DateTime? StatusUpdatedAt)
{
    public static EventDto FromEntity(IncidentEvent e) => new(
        e.EventId, e.Source, e.Service, e.Severity, e.Message, e.Status, e.Timestamp, e.ReceivedAt, e.StatusUpdatedAt);
}

/// <summary>Query parameters of GET /api/events.</summary>
public class EventFilter
{
    /// <summary>Filter by severity (INFO, WARNING, MAJOR, CRITICAL).</summary>
    public Severity? Severity { get; set; }

    /// <summary>Filter by status (OPEN, ACKNOWLEDGED, RESOLVED).</summary>
    public EventStatus? Status { get; set; }

    /// <summary>Filter by source, exact match (e.g. ATS).</summary>
    public string? Source { get; set; }

    /// <summary>Filter by service, exact match (e.g. route-service).</summary>
    public string? Service { get; set; }

    /// <summary>Case-insensitive text search in event id, message and service.</summary>
    public string? Search { get; set; }

    /// <summary>Page number, starting at 1.</summary>
    [Range(1, int.MaxValue)]
    public int Page { get; set; } = 1;

    /// <summary>Items per page (1-100).</summary>
    [Range(1, 100)]
    public int PageSize { get; set; } = 20;
}

public record PagedResult<T>(List<T> Items, int Page, int PageSize, int TotalCount);

/// <summary>Body of PUT /api/events/{eventId}/status.</summary>
public class UpdateStatusRequest
{
    /// <summary>New status: OPEN, ACKNOWLEDGED or RESOLVED.</summary>
    [Required]
    public EventStatus? Status { get; set; }
}

/// <summary>Distinct values that can be used to fill filter drop-downs.</summary>
public record EventFacetsDto(List<string> Sources, List<string> Services, List<Severity> Severities, List<EventStatus> Statuses);
