using System.Text.Json;
using System.Text.Json.Serialization;

namespace IncidentMonitoring.Core;

/// <summary>
/// One JSON format everywhere (Kafka, REST, SignalR): camelCase properties and enums as text ("CRITICAL").
/// </summary>
public static class JsonSettings
{
    public static readonly JsonSerializerOptions Options = Configure(new JsonSerializerOptions());

    public static JsonSerializerOptions Configure(JsonSerializerOptions options)
    {
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.PropertyNameCaseInsensitive = true;
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        return options;
    }
}
