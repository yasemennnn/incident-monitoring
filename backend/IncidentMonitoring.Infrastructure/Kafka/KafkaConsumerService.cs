using System.Text;
using Confluent.Kafka;
using IncidentMonitoring.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IncidentMonitoring.Infrastructure.Kafka;

/// <summary>
/// Reads events from Kafka and hands each one to <see cref="EventProcessor"/>.
///
/// - Offsets are committed manually, only after a message has been handled, so no message is lost
///   if the API stops in the middle (at-least-once delivery; duplicates are rejected by PostgreSQL).
/// - Invalid messages go straight to the dead-letter topic.
/// - Other failures (e.g. database unavailable) are retried; after MaxAttempts the message goes to the dead-letter topic.
/// </summary>
public class KafkaConsumerService(
    IServiceScopeFactory scopeFactory,
    IOptions<KafkaSettings> options,
    ILogger<KafkaConsumerService> logger) : BackgroundService
{
    private readonly KafkaSettings _settings = options.Value;

    // Consume() blocks, so the loop runs on a background thread instead of blocking application startup.
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.Run(() => ConsumeLoopAsync(stoppingToken), stoppingToken);

    private async Task ConsumeLoopAsync(CancellationToken stoppingToken)
    {
        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = _settings.BootstrapServers,
            GroupId = _settings.GroupId,
            AutoOffsetReset = AutoOffsetReset.Earliest, // a new consumer group starts at the beginning of the topic
            EnableAutoCommit = false                    // we commit ourselves after handling each message
        };
        using var consumer = new ConsumerBuilder<string?, string?>(consumerConfig).Build();
        using var deadLetterProducer = new ProducerBuilder<string?, string?>(
            new ProducerConfig { BootstrapServers = _settings.BootstrapServers, Acks = Acks.All }).Build();

        consumer.Subscribe(_settings.Topic);
        logger.LogInformation("Kafka consumer started: topic {Topic}, group {GroupId}", _settings.Topic, _settings.GroupId);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                ConsumeResult<string?, string?> result;
                try
                {
                    result = consumer.Consume(stoppingToken);
                }
                catch (ConsumeException ex)
                {
                    logger.LogError(ex, "Kafka consume error: {Reason}", ex.Error.Reason);
                    continue;
                }

                logger.LogInformation("Kafka message received: partition {Partition}, offset {Offset}",
                    result.Partition.Value, result.Offset.Value);

                await HandleMessageAsync(result, deadLetterProducer, stoppingToken);
                consumer.Commit(result);
            }
        }
        catch (OperationCanceledException)
        {
            // The application is shutting down.
        }
        finally
        {
            consumer.Close();
        }
    }

    private async Task HandleMessageAsync(
        ConsumeResult<string?, string?> result,
        IProducer<string?, string?> deadLetterProducer,
        CancellationToken stoppingToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<EventProcessor>();
                var outcome = await processor.ProcessAsync(result.Message.Value);

                switch (outcome.Outcome)
                {
                    case ProcessOutcome.Processed:
                        logger.LogInformation("Event {EventId} processed", outcome.EventId);
                        break;
                    case ProcessOutcome.Duplicate:
                        logger.LogWarning("Event {EventId} already exists, duplicate ignored", outcome.EventId);
                        break;
                    case ProcessOutcome.Invalid:
                        var reason = string.Join("; ", outcome.Errors);
                        logger.LogWarning("Invalid message at offset {Offset}: {Reason}", result.Offset.Value, reason);
                        await SendToDeadLetterTopicAsync(deadLetterProducer, result, reason);
                        break;
                }

                return;
            }
            catch (Exception ex) when (attempt < _settings.MaxAttempts && !stoppingToken.IsCancellationRequested)
            {
                var delay = TimeSpan.FromMilliseconds(_settings.RetryDelayMs * attempt);
                logger.LogWarning(ex, "Processing failed (attempt {Attempt}/{MaxAttempts}), retrying in {Delay}",
                    attempt, _settings.MaxAttempts, delay);
                await Task.Delay(delay, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Processing failed after {Attempts} attempts", attempt);
                await SendToDeadLetterTopicAsync(deadLetterProducer, result, $"processing failed after {attempt} attempts: {ex.Message}");
                return;
            }
        }
    }

    /// <summary>Publishes the original message unchanged, with the failure reason in the headers.</summary>
    private async Task SendToDeadLetterTopicAsync(
        IProducer<string?, string?> producer,
        ConsumeResult<string?, string?> failed,
        string reason)
    {
        var headers = new Headers
        {
            { "error", Encoding.UTF8.GetBytes(reason) },
            { "original-topic", Encoding.UTF8.GetBytes(failed.Topic) },
            { "original-partition", Encoding.UTF8.GetBytes(failed.Partition.Value.ToString()) },
            { "original-offset", Encoding.UTF8.GetBytes(failed.Offset.Value.ToString()) }
        };

        await producer.ProduceAsync(_settings.DeadLetterTopic, new Message<string?, string?>
        {
            Key = failed.Message.Key,
            Value = failed.Message.Value,
            Headers = headers
        });

        logger.LogWarning("Message from partition {Partition}, offset {Offset} sent to {DeadLetterTopic}",
            failed.Partition.Value, failed.Offset.Value, _settings.DeadLetterTopic);
    }
}
