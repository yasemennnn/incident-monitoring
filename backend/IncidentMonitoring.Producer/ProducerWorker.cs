using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Options;

namespace IncidentMonitoring.Producer;

/// <summary>Publishes a generated event to Kafka every IntervalMs milliseconds.</summary>
public class ProducerWorker(
    IOptions<ProducerSettings> options,
    IHostApplicationLifetime lifetime,
    ILogger<ProducerWorker> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web); // camelCase

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        var config = new ProducerConfig
        {
            BootstrapServers = settings.BootstrapServers,
            Acks = Acks.All,          // wait until the broker has stored the message
            EnableIdempotence = true  // producer retries never create duplicates
        };
        using var producer = new ProducerBuilder<string, string>(config).Build();

        logger.LogInformation("Producing to {Topic} every {IntervalMs} ms", settings.Topic, settings.IntervalMs);

        var sent = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            var message = EventGenerator.Create();
            try
            {
                // The service name is the message key, so all events of one service go to the same
                // partition and are consumed in order.
                var result = await producer.ProduceAsync(settings.Topic, new Message<string, string>
                {
                    Key = message.Service,
                    Value = JsonSerializer.Serialize(message, JsonOptions)
                }, stoppingToken);

                logger.LogInformation("Published {EventId} {Severity} {Status} {Service} to partition {Partition}",
                    message.EventId, message.Severity, message.Status, message.Service, result.Partition.Value);
            }
            catch (ProduceException<string, string> ex)
            {
                logger.LogError(ex, "Failed to publish {EventId}", message.EventId);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            sent++;
            if (settings.Count > 0 && sent >= settings.Count)
            {
                logger.LogInformation("Sent {Count} events, stopping", sent);
                lifetime.StopApplication();
                break;
            }

            try
            {
                await Task.Delay(settings.IntervalMs, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        producer.Flush(TimeSpan.FromSeconds(5));
    }
}
