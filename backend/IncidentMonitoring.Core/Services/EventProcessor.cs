using System.Text.Json;
using IncidentMonitoring.Core.Dtos;
using IncidentMonitoring.Core.Interfaces;
using IncidentMonitoring.Core.Models;
using IncidentMonitoring.Core.Rules;

namespace IncidentMonitoring.Core.Services;

public enum ProcessOutcome
{
    Processed,
    Duplicate,
    Invalid,
    /// <summary>An event with the same id but different content is already stored.</summary>
    Conflict
}

/// <param name="Errors">Validation errors for Invalid; the differing field names for Conflict.</param>
/// <param name="StatusDiffers">For Duplicate: the stored status is not the status in this message (it was changed through the API).</param>
public record ProcessResult(ProcessOutcome Outcome, string? EventId, List<string> Errors, bool StatusDiffers = false);

/// <summary>
/// Handles one Kafka message: deserialize → validate → store in PostgreSQL → request a dashboard rebuild → notify clients.
/// Exceptions from PostgreSQL are not caught here; the Kafka consumer retries them. Redis is not used here at all.
/// </summary>
public class EventProcessor(
    IEventRepository repository,
    IDashboardRefresher dashboardRefresher,
    IEventNotifier notifier,
    TimeProvider timeProvider,
    EventValidationOptions validationOptions)
{
    public async Task<ProcessResult> ProcessAsync(string? json)
    {
        // 1. Deserialize
        EventMessage? message;
        try
        {
            message = string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<EventMessage>(json, JsonSettings.Options);
        }
        catch (JsonException ex)
        {
            return new ProcessResult(ProcessOutcome.Invalid, null, [$"invalid JSON: {ex.Message}"]);
        }

        if (message is null)
            return new ProcessResult(ProcessOutcome.Invalid, null, ["message is empty"]);

        // 2. Validate
        var validation = EventValidator.Validate(message, timeProvider.GetUtcNow(), validationOptions.MaxFutureSkew);
        if (!validation.IsValid)
            return new ProcessResult(ProcessOutcome.Invalid, message.EventId, validation.Errors);

        var incidentEvent = validation.Event!;

        // 3. Store in PostgreSQL. The primary key on EventId rejects an id that is already stored; such a message
        //    stops here and changes nothing.
        if (!await repository.AddAsync(incidentEvent))
            return await ClassifyExistingIdAsync(incidentEvent);

        // 4. The event is stored. The dashboard is rebuilt from PostgreSQL in the background; this only sets a
        //    flag, so a Redis outage cannot fail the message.
        dashboardRefresher.RequestRefresh();

        // 5. Notify connected dashboards
        await notifier.EventReceivedAsync(EventDto.FromEntity(incidentEvent));

        return new ProcessResult(ProcessOutcome.Processed, incidentEvent.EventId, []);
    }

    /// <summary>
    /// Decides whether a message whose id is already stored is the same event delivered again (Duplicate) or a
    /// different event reusing the id (Conflict). The stored row is never changed here.
    /// </summary>
    private async Task<ProcessResult> ClassifyExistingIdAsync(IncidentEvent incoming)
    {
        var stored = await repository.GetAsync(incoming.EventId)
            ?? throw new InvalidOperationException(
                $"The insert reported that event '{incoming.EventId}' already exists, but it could not be loaded.");

        var differences = EventComparison.DifferingImmutableFields(stored, incoming);
        if (differences.Count > 0)
            return new ProcessResult(ProcessOutcome.Conflict, incoming.EventId, differences);

        // The same event again. Its status may have been changed through the API since; the stored status wins.
        return new ProcessResult(ProcessOutcome.Duplicate, incoming.EventId, [], StatusDiffers: stored.Status != incoming.Status);
    }
}
