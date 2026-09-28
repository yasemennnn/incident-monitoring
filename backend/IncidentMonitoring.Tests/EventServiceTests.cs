using IncidentMonitoring.Core;
using IncidentMonitoring.Core.Models;
using IncidentMonitoring.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace IncidentMonitoring.Tests;

public class EventServiceTests
{
    // 2026-09-28T14:35:12.1234567Z; PostgreSQL keeps microseconds, so the last digit must be dropped.
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 9, 28, 14, 35, 12, TimeSpan.Zero).AddTicks(1_234_567);
    private static readonly DateTime NowInMicroseconds = new DateTime(2026, 9, 28, 14, 35, 12, DateTimeKind.Utc).AddTicks(1_234_560);
    private static readonly DateTime EarlierUpdate = new(2026, 9, 28, 9, 0, 0, DateTimeKind.Utc);

    private readonly FakeEventRepository _repository = new();
    private readonly FakeDashboardRefresher _refresher = new();
    private readonly FakeNotifier _notifier = new();

    public EventServiceTests()
    {
        _repository.Events["EVT-1"] = new IncidentEvent
        {
            EventId = "EVT-1",
            Source = "ATS",
            Service = "route-service",
            Severity = Severity.CRITICAL,
            Message = "Route locking failed",
            Status = EventStatus.OPEN,
            Timestamp = new DateTime(2026, 9, 28, 8, 0, 0, DateTimeKind.Utc),
            StatusUpdatedAt = EarlierUpdate
        };
    }

    private EventService CreateService() =>
        new(_repository, _refresher, _notifier, new FixedTimeProvider(Now), NullLogger<EventService>.Instance);

    private void AssertNoSideEffects()
    {
        Assert.Equal(0, _refresher.Requests);
        Assert.Empty(_notifier.Updated);
    }

    [Fact]
    public async Task Unknown_event_is_not_found()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => CreateService().UpdateStatusAsync("EVT-404", EventStatus.RESOLVED));

        Assert.Empty(_repository.StatusUpdates);
        AssertNoSideEffects();
    }

    [Fact]
    public async Task Same_status_returns_the_current_event_and_changes_nothing()
    {
        var result = await CreateService().UpdateStatusAsync("EVT-1", EventStatus.OPEN);

        Assert.Equal(EventStatus.OPEN, result.Status);
        Assert.Equal(EarlierUpdate, result.StatusUpdatedAt);
        Assert.Empty(_repository.StatusUpdates);
        Assert.Equal(EarlierUpdate, _repository.Events["EVT-1"].StatusUpdatedAt);
        AssertNoSideEffects();
    }

    [Fact]
    public async Task Invalid_transition_is_rejected_and_nothing_changes()
    {
        _repository.Events["EVT-1"].Status = EventStatus.RESOLVED;

        await Assert.ThrowsAsync<InvalidStatusTransitionException>(() =>
            CreateService().UpdateStatusAsync("EVT-1", EventStatus.ACKNOWLEDGED));

        Assert.Empty(_repository.StatusUpdates);
        AssertNoSideEffects();
    }

    [Fact]
    public async Task Winning_update_is_stored_then_a_refresh_is_requested_and_it_is_notified_once()
    {
        var result = await CreateService().UpdateStatusAsync("EVT-1", EventStatus.RESOLVED);

        Assert.Equal(("EVT-1", EventStatus.OPEN, EventStatus.RESOLVED, NowInMicroseconds), Assert.Single(_repository.StatusUpdates));
        Assert.Equal(EventStatus.RESOLVED, result.Status);
        Assert.Equal(NowInMicroseconds, result.StatusUpdatedAt);
        Assert.Equal(EventStatus.RESOLVED, _repository.Events["EVT-1"].Status);
        Assert.Equal(1, _refresher.Requests);
        Assert.Equal(EventStatus.RESOLVED, Assert.Single(_notifier.Updated).Status);
    }

    [Fact]
    public async Task Lost_update_because_the_event_was_deleted_is_not_found()
    {
        _repository.BeforeStatusUpdate = () => _repository.Events.Remove("EVT-1");

        await Assert.ThrowsAsync<NotFoundException>(() => CreateService().UpdateStatusAsync("EVT-1", EventStatus.RESOLVED));

        AssertNoSideEffects();
    }

    [Fact]
    public async Task Lost_update_to_the_same_target_returns_the_stored_state_without_side_effects()
    {
        var otherRequestTime = new DateTime(2026, 9, 28, 14, 35, 11, DateTimeKind.Utc);
        _repository.BeforeStatusUpdate = () =>
        {
            _repository.Events["EVT-1"].Status = EventStatus.RESOLVED;
            _repository.Events["EVT-1"].StatusUpdatedAt = otherRequestTime;
        };

        var result = await CreateService().UpdateStatusAsync("EVT-1", EventStatus.RESOLVED);

        Assert.Equal(EventStatus.RESOLVED, result.Status);
        Assert.Equal(otherRequestTime, result.StatusUpdatedAt);
        AssertNoSideEffects();
    }

    [Fact]
    public async Task Lost_update_to_another_status_is_a_conflict()
    {
        // Both requests read OPEN. The other one acknowledged first; this one wanted RESOLVED.
        _repository.BeforeStatusUpdate = () => _repository.Events["EVT-1"].Status = EventStatus.ACKNOWLEDGED;

        var error = await Assert.ThrowsAsync<InvalidStatusTransitionException>(() =>
            CreateService().UpdateStatusAsync("EVT-1", EventStatus.RESOLVED));

        Assert.Contains("concurrently", error.Message);
        Assert.Equal(EventStatus.ACKNOWLEDGED, _repository.Events["EVT-1"].Status);
        AssertNoSideEffects();
    }

    [Fact]
    public async Task Recent_events_are_newest_first_by_event_time_then_event_id()
    {
        var t = new DateTime(2026, 9, 28, 10, 0, 0, DateTimeKind.Utc);
        AddEvent("EVT-A", t.AddMinutes(-5));
        AddEvent("EVT-B", t);
        AddEvent("EVT-C", t);                // same time as EVT-B: the higher id comes first
        AddEvent("EVT-D", t.AddMinutes(-1));

        var recent = await CreateService().GetRecentEventsAsync(4);

        // EVT-1 (08:00, from the constructor) is the oldest and falls outside the 4 requested.
        Assert.Equal(["EVT-C", "EVT-B", "EVT-D", "EVT-A"], recent.Select(e => e.EventId));
    }

    [Fact]
    public async Task Recent_events_return_at_most_the_requested_count()
    {
        AddEvent("EVT-2", new DateTime(2026, 9, 28, 9, 0, 0, DateTimeKind.Utc));

        var recent = await CreateService().GetRecentEventsAsync(1);

        Assert.Equal("EVT-2", Assert.Single(recent).EventId);
    }

    private void AddEvent(string eventId, DateTime timestamp) => _repository.Events[eventId] = new IncidentEvent
    {
        EventId = eventId,
        Source = "ATS",
        Service = "route-service",
        Severity = Severity.INFO,
        Message = "Event",
        Status = EventStatus.OPEN,
        Timestamp = timestamp
    };
}
