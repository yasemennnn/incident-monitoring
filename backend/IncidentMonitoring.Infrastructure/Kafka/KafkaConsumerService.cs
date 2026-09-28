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
/// - Invalid and Conflict messages go straight to the dead-letter topic.
/// - PostgreSQL temporarily unavailable: nothing is committed and nothing goes to the dead-letter topic.
///   The consumer seeks back to the same offset and reads it again after a growing delay (capped at
///   MaxRetryDelayMs), for as long as the outage lasts.
/// - Any other failure is retried; after MaxAttempts the message goes to the dead-letter topic.
/// </summary>
public class KafkaConsumerService(
    IServiceScopeFactory scopeFactory,
    IOptions<KafkaSettings> options,
    ILogger<KafkaConsumerService> logger) : BackgroundService
{
    private readonly KafkaSettings _settings = options.Value;

    /// <summary>What the consume loop must do with a record after <see cref="HandleMessageAsync"/>.</summary>
    private enum HandleResult
    {
        /// <summary>Processed or Duplicate: PostgreSQL was reached. Commit.</summary>
        Stored,

        /// <summary>Invalid, Conflict, or failed MaxAttempts times: published to the dead-letter topic. Commit.</summary>
        DeadLettered,

        /// <summary>PostgreSQL is temporarily unavailable. Do not commit; read the same offset again later.</summary>
        RetryLater
    }

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

        // Consecutive transient PostgreSQL failures across all partitions: the database is shared, so one outage
        // slows down every partition. Reset only when a record has reached PostgreSQL and its offset is committed.
        var transientFailures = 0;

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

                var handled = await HandleMessageAsync(result, deadLetterProducer, stoppingToken);

                if (handled == HandleResult.RetryLater)
                {
                    // Nothing is committed. Seeking back makes the next Consume() return this same record, instead of
                    // moving on and later committing past it. The wait happens here, between two Consume() calls,
                    // so the consumer stays in its group during a long outage.
                    transientFailures++;
                    consumer.Seek(result.TopicPartitionOffset);
                    var delay = RetryBackoff.Delay(transientFailures,
                        TimeSpan.FromMilliseconds(_settings.RetryDelayMs),
                        TimeSpan.FromMilliseconds(_settings.MaxRetryDelayMs));
                    logger.LogWarning(
                        "PostgreSQL unavailable (transient failure {TransientFailures}); partition {Partition}, offset {Offset} will be read again in {Delay}",
                        transientFailures, result.Partition.Value, result.Offset.Value, delay);
                    await Task.Delay(delay, stoppingToken);
                    continue;
                }

                try
                {
                    consumer.Commit(result);
                }
                catch (KafkaException ex)
                {
                    // The record was handled, only its offset was not saved. After a restart or rebalance it is
                    // delivered again and becomes a Duplicate, so there is nothing else to do here.
                    logger.LogWarning(ex, "Kafka commit failed for partition {Partition}, offset {Offset}; the record may be delivered again",
                        result.Partition.Value, result.Offset.Value);
                    continue;
                }

                if (handled == HandleResult.Stored)
                    transientFailures = 0;
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

    private async Task<HandleResult> HandleMessageAsync(
        ConsumeResult<string?, string?> result,
        IProducer<string?, string?> deadLetterProducer,
        CancellationToken stoppingToken)
    {
        // 1. Process the message. The retries here cover only the processing itself; nothing is published to
        //    the dead-letter topic inside this loop, so a dead-letter failure can never cause another attempt.
        ProcessResult? outcome = null;
        string? exhaustedReason = null;
        for (var attempt = 1; outcome is null; attempt++)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<EventProcessor>();
                outcome = await processor.ProcessAsync(result.Message.Value);
            }
            catch (Exception ex) when (TransientFailure.IsTransient(ex))
            {
                // Checked first: a database outage is not a problem of this message, so it never counts as an
                // attempt and never sends a valid event to the dead-letter topic. The consume loop reads it again.
                logger.LogWarning(ex, "PostgreSQL temporarily unavailable while processing partition {Partition}, offset {Offset}",
                    result.Partition.Value, result.Offset.Value);
                return HandleResult.RetryLater;
            }
            catch (Exception ex) when (attempt < _settings.MaxAttempts && !stoppingToken.IsCancellationRequested)
            {
                var delay = RetryBackoff.Delay(attempt,
                    TimeSpan.FromMilliseconds(_settings.RetryDelayMs),
                    TimeSpan.FromMilliseconds(_settings.MaxRetryDelayMs));
                logger.LogWarning(ex, "Processing failed (attempt {Attempt}/{MaxAttempts}), retrying in {Delay}",
                    attempt, _settings.MaxAttempts, delay);
                await Task.Delay(delay, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Processing failed after {Attempts} attempts", attempt);
                exhaustedReason = $"processing failed after {attempt} attempts: {ex.Message}";
                break;
            }
        }

        // 2. Act on the result, once. If a dead-letter publish throws, the exception leaves this method: the message
        //    is not processed again and its offset is not committed, so it is delivered again after a restart.
        if (outcome is null)
        {
            await SendToDeadLetterTopicAsync(deadLetterProducer, result, exhaustedReason!);
            return HandleResult.DeadLettered;
        }

        switch (outcome.Outcome)
        {
            case ProcessOutcome.Processed:
                logger.LogInformation("Event {EventId} processed", outcome.EventId);
                return HandleResult.Stored;
            case ProcessOutcome.Duplicate when outcome.StatusDiffers:
                logger.LogWarning(
                    "Event {EventId} redelivered (partition {Partition}, offset {Offset}) with a different status than the stored one; duplicate ignored, stored status kept",
                    outcome.EventId, result.Partition.Value, result.Offset.Value);
                return HandleResult.Stored;
            case ProcessOutcome.Duplicate:
                logger.LogInformation("Event {EventId} already stored (partition {Partition}, offset {Offset}); duplicate ignored",
                    outcome.EventId, result.Partition.Value, result.Offset.Value);
                return HandleResult.Stored;
            case ProcessOutcome.Conflict:
                var fields = string.Join(", ", outcome.Errors);
                logger.LogError(
                    "Event {EventId} (partition {Partition}, offset {Offset}) conflicts with the stored event with the same id; differing fields: {Fields}",
                    outcome.EventId, result.Partition.Value, result.Offset.Value, fields);
                await SendToDeadLetterTopicAsync(deadLetterProducer, result,
                    $"eventId conflict: event {outcome.EventId} is already stored with a different {fields}");
                return HandleResult.DeadLettered;
            case ProcessOutcome.Invalid:
                var reason = string.Join("; ", outcome.Errors);
                logger.LogWarning("Invalid message at offset {Offset}: {Reason}", result.Offset.Value, reason);
                await SendToDeadLetterTopicAsync(deadLetterProducer, result, reason);
                return HandleResult.DeadLettered;
            default:
                throw new InvalidOperationException($"Unknown process outcome {outcome.Outcome}");
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
