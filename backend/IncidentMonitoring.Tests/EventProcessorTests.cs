using IncidentMonitoring.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace IncidentMonitoring.Tests;

public class EventProcessorTests
{
    private const string ValidJson = """
        {"eventId":"EVT-10001","source":"ATS","service":"route-service","severity":"CRITICAL",
         "message":"Route locking failed","status":"OPEN","timestamp":"2026-06-01T10:15:00Z"}
        """;

    private readonly FakeEventRepository _repository = new();
    private readonly FakeDashboardStore _dashboardStore = new();
    private readonly FakeNotifier _notifier = new();

    private EventProcessor CreateProcessor() =>
        new(_repository, _dashboardStore, _notifier, NullLogger<EventProcessor>.Instance);

    [Fact]
    public async Task Valid_event_is_stored_counted_in_redis_and_pushed()
    {
        var result = await CreateProcessor().ProcessAsync(ValidJson);

        Assert.Equal(ProcessOutcome.Processed, result.Outcome);
        Assert.True(_repository.Events.ContainsKey("EVT-10001"));
        Assert.Equal(new[] { "EVT-10001" }, _dashboardStore.AddedEventIds);
        Assert.Single(_notifier.Received);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("")]
    [InlineData("""{"eventId":"EVT-1","severity":"FATAL"}""")]
    public async Task Invalid_message_is_rejected_and_nothing_is_stored(string json)
    {
        var result = await CreateProcessor().ProcessAsync(json);

        Assert.Equal(ProcessOutcome.Invalid, result.Outcome);
        Assert.NotEmpty(result.Errors);
        Assert.Empty(_repository.Events);
        Assert.Empty(_dashboardStore.AddedEventIds);
    }

    [Fact]
    public async Task Duplicate_event_does_not_update_redis_counters_twice()
    {
        var processor = CreateProcessor();

        await processor.ProcessAsync(ValidJson);
        var second = await processor.ProcessAsync(ValidJson);

        Assert.Equal(ProcessOutcome.Duplicate, second.Outcome);
        Assert.Single(_repository.Events);
        Assert.Single(_dashboardStore.AddedEventIds);
        Assert.Single(_notifier.Received);
    }

    [Fact]
    public async Task Event_is_still_stored_when_redis_is_down()
    {
        _dashboardStore.IsDown = true;

        var result = await CreateProcessor().ProcessAsync(ValidJson);

        Assert.Equal(ProcessOutcome.Processed, result.Outcome);
        Assert.True(_repository.Events.ContainsKey("EVT-10001"));
    }

    [Fact]
    public async Task Database_error_is_thrown_so_the_consumer_can_retry()
    {
        _repository.ThrowOnAdd = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateProcessor().ProcessAsync(ValidJson));
    }
}
