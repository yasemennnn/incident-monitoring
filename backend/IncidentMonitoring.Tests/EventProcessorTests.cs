using IncidentMonitoring.Core.Models;
using IncidentMonitoring.Core.Rules;
using IncidentMonitoring.Core.Services;

namespace IncidentMonitoring.Tests;

public class EventProcessorTests
{
    private const string ValidJson = """
        {"eventId":"EVT-10001","source":"ATS","service":"route-service","severity":"CRITICAL",
         "message":"Route locking failed","status":"OPEN","timestamp":"2026-06-01T10:15:00Z"}
        """;

    private readonly FakeEventRepository _repository = new();
    private readonly FakeDashboardRefresher _refresher = new();
    private readonly FakeNotifier _notifier = new();

    private EventProcessor CreateProcessor() =>
        new(_repository, _refresher, _notifier,
            new FixedTimeProvider(new DateTimeOffset(2026, 6, 1, 10, 16, 0, TimeSpan.Zero)),
            new EventValidationOptions());

    /// <summary>The event in <see cref="ValidJson"/> as it would already be stored in PostgreSQL.</summary>
    private static IncidentEvent StoredEvent(EventStatus status = EventStatus.OPEN) => new()
    {
        EventId = "EVT-10001",
        Source = "ATS",
        Service = "route-service",
        Severity = Severity.CRITICAL,
        Message = "Route locking failed",
        Status = status,
        Timestamp = new DateTime(2026, 6, 1, 10, 15, 0, DateTimeKind.Utc),
        ReceivedAt = new DateTime(2026, 6, 1, 10, 15, 1, DateTimeKind.Utc)
    };

    private void AssertNoSideEffects()
    {
        Assert.Equal(0, _refresher.Requests);
        Assert.Empty(_notifier.Received);
    }

    [Fact]
    public async Task Valid_event_is_stored_then_a_refresh_is_requested_and_it_is_pushed()
    {
        var result = await CreateProcessor().ProcessAsync(ValidJson);

        Assert.Equal(ProcessOutcome.Processed, result.Outcome);
        Assert.True(_repository.Events.ContainsKey("EVT-10001"));
        Assert.Equal(1, _refresher.Requests);
        Assert.Equal("EVT-10001", Assert.Single(_notifier.Received).EventId);
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
        AssertNoSideEffects();
    }

    // A. The same message delivered twice.
    [Fact]
    public async Task Redelivered_event_is_a_duplicate_without_side_effects()
    {
        var processor = CreateProcessor();

        await processor.ProcessAsync(ValidJson);
        var second = await processor.ProcessAsync(ValidJson);

        Assert.Equal(ProcessOutcome.Duplicate, second.Outcome);
        Assert.False(second.StatusDiffers);
        Assert.Single(_repository.Events);
        Assert.Equal(1, _refresher.Requests); // only from the first delivery
        Assert.Single(_notifier.Received);
    }

    // B. A different event reusing a stored id.
    [Fact]
    public async Task Same_id_with_different_content_is_a_conflict_and_the_stored_event_is_unchanged()
    {
        var stored = StoredEvent();
        stored.Source = "SCADA";
        stored.Message = "Power supply failure on section";
        _repository.Events["EVT-10001"] = stored;

        var result = await CreateProcessor().ProcessAsync(ValidJson);

        Assert.Equal(ProcessOutcome.Conflict, result.Outcome);
        Assert.Equal(["source", "message"], result.Errors);
        Assert.Equal("SCADA", _repository.Events["EVT-10001"].Source);
        Assert.Equal("Power supply failure on section", _repository.Events["EVT-10001"].Message);
        AssertNoSideEffects();
    }

    // C. The same event, but the stored status is not the one in the message.
    [Fact]
    public async Task Same_event_with_a_different_stored_status_is_a_duplicate_and_keeps_the_stored_status()
    {
        _repository.Events["EVT-10001"] = StoredEvent(EventStatus.RESOLVED);

        var result = await CreateProcessor().ProcessAsync(ValidJson);

        Assert.Equal(ProcessOutcome.Duplicate, result.Outcome);
        Assert.True(result.StatusDiffers);
        Assert.Equal(EventStatus.RESOLVED, _repository.Events["EVT-10001"].Status);
        AssertNoSideEffects();
    }

    // D. Stored as OPEN, acknowledged by a user through the API, then the original OPEN message is redelivered.
    [Fact]
    public async Task Redelivered_original_event_does_not_undo_a_status_change_made_through_the_api()
    {
        var processor = CreateProcessor();
        await processor.ProcessAsync(ValidJson);
        _repository.Events["EVT-10001"].Status = EventStatus.ACKNOWLEDGED;

        var result = await processor.ProcessAsync(ValidJson);

        Assert.Equal(ProcessOutcome.Duplicate, result.Outcome);
        Assert.True(result.StatusDiffers);
        Assert.Equal(EventStatus.ACKNOWLEDGED, _repository.Events["EVT-10001"].Status);
        Assert.Equal(1, _refresher.Requests); // only from the first delivery
        Assert.Single(_notifier.Received);
    }

    // E. The insert says the id exists, but the row cannot be read: neither Duplicate nor Conflict.
    [Fact]
    public async Task Existing_id_that_cannot_be_loaded_throws()
    {
        _repository.ReportDuplicateWithoutRow = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateProcessor().ProcessAsync(ValidJson));

        AssertNoSideEffects();
    }

    [Fact]
    public async Task Database_error_is_thrown_so_the_consumer_can_retry()
    {
        _repository.ThrowOnAdd = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateProcessor().ProcessAsync(ValidJson));

        AssertNoSideEffects(); // nothing is stored, so there is nothing to refresh or push
    }
}
