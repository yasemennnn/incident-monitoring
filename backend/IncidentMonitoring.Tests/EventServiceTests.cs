using IncidentMonitoring.Core;
using IncidentMonitoring.Core.Models;
using IncidentMonitoring.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace IncidentMonitoring.Tests;

public class EventServiceTests
{
    private readonly FakeEventRepository _repository = new();
    private readonly FakeDashboardStore _dashboardStore = new();
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
            Timestamp = DateTime.UtcNow
        };
    }

    private EventService CreateService() =>
        new(_repository, _dashboardStore, _notifier, NullLogger<EventService>.Instance);

    [Fact]
    public async Task Status_update_saves_to_database_moves_redis_counters_and_notifies()
    {
        var result = await CreateService().UpdateStatusAsync("EVT-1", EventStatus.RESOLVED);

        Assert.Equal(EventStatus.RESOLVED, result.Status);
        Assert.NotNull(result.StatusUpdatedAt);
        Assert.Equal(1, _repository.SaveChangesCount);
        Assert.Equal((EventStatus.OPEN, EventStatus.RESOLVED), Assert.Single(_dashboardStore.StatusChanges));
        Assert.Single(_notifier.Updated);
    }

    [Fact]
    public async Task Status_update_of_unknown_event_throws_not_found()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => CreateService().UpdateStatusAsync("EVT-404", EventStatus.RESOLVED));
    }

    [Fact]
    public async Task Invalid_status_change_is_rejected_and_nothing_changes()
    {
        _repository.Events["EVT-1"].Status = EventStatus.RESOLVED;

        await Assert.ThrowsAsync<InvalidStatusTransitionException>(() =>
            CreateService().UpdateStatusAsync("EVT-1", EventStatus.ACKNOWLEDGED));

        Assert.Equal(0, _repository.SaveChangesCount);
        Assert.Empty(_dashboardStore.StatusChanges);
        Assert.Empty(_notifier.Updated);
    }
}
