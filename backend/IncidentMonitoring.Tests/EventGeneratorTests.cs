using System.Text.Json;
using IncidentMonitoring.Core;
using IncidentMonitoring.Core.Rules;
using IncidentMonitoring.Producer;
using ConsumerMessage = IncidentMonitoring.Core.Dtos.EventMessage;

namespace IncidentMonitoring.Tests;

public class EventGeneratorTests
{
    [Fact]
    public void Generated_event_id_is_EVT_plus_a_full_uppercase_uuid()
    {
        var eventId = EventGenerator.Create().EventId;

        Assert.Equal(36, eventId.Length);
        Assert.StartsWith("EVT-", eventId);
        Assert.All(eventId[4..], c => Assert.True(char.IsAsciiHexDigitUpper(c), $"'{c}' is not uppercase hex"));
    }

    [Fact]
    public void Generated_event_passes_the_consumer_validation()
    {
        // Same path as in production: the producer serializes the event, the consumer deserializes and validates it.
        var json = JsonSerializer.Serialize(EventGenerator.Create(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var message = JsonSerializer.Deserialize<ConsumerMessage>(json, JsonSettings.Options)!;

        var result = EventValidator.Validate(message, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5));

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$", message.Timestamp);
    }
}
