using System.Text.Json;
using IncidentMonitoring.Core.Dtos;
using IncidentMonitoring.Core.Interfaces;
using IncidentMonitoring.Core.Rules;
using Microsoft.Extensions.Logging;

namespace IncidentMonitoring.Core.Services;

public enum ProcessOutcome
{
    Processed,
    Duplicate,
    Invalid
}

public record ProcessResult(ProcessOutcome Outcome, string? EventId, List<string> Errors);

/// <summary>
/// Handles one Kafka message: deserialize → validate → store in PostgreSQL → update Redis → notify clients.
/// Exceptions from PostgreSQL are not caught here; the Kafka consumer retries them.
/// </summary>
public class EventProcessor(
    IEventRepository repository,
    IDashboardStore dashboardStore,
    IEventNotifier notifier,
    ILogger<EventProcessor> logger)
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
        var validation = EventValidator.Validate(message);
        if (!validation.IsValid)
            return new ProcessResult(ProcessOutcome.Invalid, message.EventId, validation.Errors);

        var incidentEvent = validation.Event!;

        // 3. Store in PostgreSQL. The primary key on EventId rejects duplicates, and a duplicate
        //    stops here, so Redis counters are never incremented twice for the same event.
        if (!await repository.AddAsync(incidentEvent))
            return new ProcessResult(ProcessOutcome.Duplicate, incidentEvent.EventId, []);

        // 4. Update Redis. The event is already stored safely, so a Redis failure is logged
        //    instead of failing the message (see "Known limitations" in the README).
        try
        {
            await dashboardStore.AddEventAsync(incidentEvent);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Redis update failed for event {EventId}; dashboard counters do not include it", incidentEvent.EventId);
        }

        // 5. Notify connected dashboards
        await notifier.EventReceivedAsync(EventDto.FromEntity(incidentEvent));

        return new ProcessResult(ProcessOutcome.Processed, incidentEvent.EventId, []);
    }
}
