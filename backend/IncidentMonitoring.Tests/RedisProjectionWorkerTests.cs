using IncidentMonitoring.Core.Dtos;
using IncidentMonitoring.Core.Interfaces;
using IncidentMonitoring.Core.Models;
using IncidentMonitoring.Infrastructure.Redis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace IncidentMonitoring.Tests;

// Runs the real worker against in-memory fakes. These tests prove the scheduling (startup, requests, periodic
// refresh, retry); they do not prove anything about real Redis or PostgreSQL.
public class RedisProjectionWorkerTests : IAsyncLifetime
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private readonly FakeEventRepository _repository = new()
    {
        Snapshot = new DashboardSnapshot(
            [new EventCountRow("route-service", EventStatus.OPEN, Severity.CRITICAL, 2)],
            [new LatestServiceEvent("route-service", new DateTime(2026, 9, 28, 14, 0, 0, DateTimeKind.Utc), Severity.CRITICAL)],
            new DateTime(2026, 9, 28, 15, 0, 0, DateTimeKind.Utc))
    };
    private readonly FakeDashboardStore _store = new();
    private readonly FakeNotifier _notifier = new();
    private RedisProjectionWorker? _worker;

    private RedisProjectionWorker CreateWorker(int refreshIntervalSeconds = 3600)
    {
        var services = new ServiceCollection();
        services.AddScoped<IEventRepository>(_ => _repository);
        services.AddSingleton<IDashboardStore>(_store);
        services.AddScoped<IEventNotifier>(_ => _notifier);
        var provider = services.BuildServiceProvider();

        _worker = new RedisProjectionWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new DashboardProjectionSettings { RefreshIntervalSeconds = refreshIntervalSeconds }),
            NullLogger<RedisProjectionWorker>.Instance);
        return _worker;
    }

    private async Task<RedisProjectionWorker> StartWorkerAsync(int refreshIntervalSeconds = 3600)
    {
        var worker = CreateWorker(refreshIntervalSeconds);
        await worker.StartAsync(CancellationToken.None);
        return worker;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("The condition was not reached in time.");
            await Task.Delay(10);
        }
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_worker is not null)
            await _worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Startup_writes_the_projection_built_from_the_snapshot()
    {
        await StartWorkerAsync();

        await WaitUntilAsync(() => _store.Snapshots.Count == 1);
        var state = _store.Snapshots[0];
        Assert.Equal(2, state.Counters.Total);
        Assert.Equal(2, state.CriticalEvents);
        Assert.Equal(_repository.Snapshot.SnapshotAt, state.SnapshotAt);
        Assert.Equal("route-service", Assert.Single(state.Services).Name);
    }

    [Fact]
    public async Task Summary_is_pushed_from_the_state_that_was_written()
    {
        await StartWorkerAsync();

        await WaitUntilAsync(() => _notifier.Summaries.Count == 1);
        var written = Assert.Single(_store.Snapshots);
        var summary = _notifier.Summaries[0];
        var expected = DashboardSummaryDto.From(written);
        Assert.Equal(expected.TotalEvents, summary.TotalEvents);
        Assert.Equal(expected.OpenEvents, summary.OpenEvents);
        Assert.Equal(expected.CriticalEvents, summary.CriticalEvents);
        Assert.Equal(expected.SeverityDistribution, summary.SeverityDistribution);
        Assert.Equal(expected.StatusDistribution, summary.StatusDistribution);
        Assert.Equal(expected.Services, summary.Services);
        Assert.Equal(written.SnapshotAt, summary.SnapshotAt);
    }

    [Fact]
    public async Task No_summary_is_pushed_when_the_redis_write_fails()
    {
        _store.FailingSnapshotWrites = 1;

        await StartWorkerAsync();

        await WaitUntilAsync(() => _store.SnapshotWriteAttempts == 1);
        Assert.Empty(_notifier.Summaries);                       // the failed write sent nothing
        await WaitUntilAsync(() => _notifier.Summaries.Count == 1); // the successful retry does
        Assert.Single(_store.Snapshots);
    }

    [Fact]
    public async Task Failed_summary_notification_does_not_make_the_rebuild_fail()
    {
        _notifier.FailSummaries = true;

        await StartWorkerAsync();

        await WaitUntilAsync(() => _store.Snapshots.Count == 1);
        await Task.Delay(1500); // longer than the first retry delay (1 second)
        Assert.Equal(1, _store.SnapshotWriteAttempts);          // no retry was started
        Assert.Equal(1, _repository.SnapshotReads);
    }

    [Fact]
    public async Task Request_after_startup_causes_another_rebuild()
    {
        var worker = await StartWorkerAsync();
        await WaitUntilAsync(() => _store.Snapshots.Count == 1);

        worker.RequestRefresh();

        await WaitUntilAsync(() => _store.Snapshots.Count == 2);
    }

    [Fact]
    public async Task Requests_during_a_rebuild_cause_exactly_one_follow_up_rebuild()
    {
        var holdFirstRebuild = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _repository.BeforeSnapshot = () => _repository.SnapshotReads == 1 ? holdFirstRebuild.Task : Task.CompletedTask;
        var worker = await StartWorkerAsync();
        await WaitUntilAsync(() => _repository.SnapshotReads == 1); // the startup rebuild is reading PostgreSQL

        for (var i = 0; i < 10; i++)
            worker.RequestRefresh();
        holdFirstRebuild.SetResult();

        await WaitUntilAsync(() => _store.Snapshots.Count == 2);
        await Task.Delay(300);
        Assert.Equal(2, _store.Snapshots.Count); // ten requests, one follow-up rebuild
    }

    [Fact]
    public async Task Rebuild_happens_periodically_without_requests()
    {
        await StartWorkerAsync(refreshIntervalSeconds: 1);

        await WaitUntilAsync(() => _store.Snapshots.Count >= 2);
    }

    [Fact]
    public async Task Failed_write_is_retried_after_the_backoff()
    {
        _store.FailingSnapshotWrites = 1;

        await StartWorkerAsync();

        await WaitUntilAsync(() => _store.Snapshots.Count == 1); // the first retry waits 1 second
        Assert.Equal(2, _store.SnapshotWriteAttempts);
        Assert.Equal(2, _repository.SnapshotReads);              // the retry reads a fresh snapshot
    }

    [Fact]
    public async Task Requests_do_not_shorten_the_retry_wait()
    {
        _store.FailingSnapshotWrites = 1;
        var worker = await StartWorkerAsync();
        await WaitUntilAsync(() => _store.SnapshotWriteAttempts == 1); // failed; now waiting 1 second

        for (var i = 0; i < 10; i++)
            worker.RequestRefresh();
        await Task.Delay(400);

        Assert.Equal(1, _store.SnapshotWriteAttempts);
        await WaitUntilAsync(() => _store.Snapshots.Count == 1); // retried once the wait is over
    }

    [Fact]
    public async Task Restored_redis_connection_ends_the_retry_wait()
    {
        _store.FailingSnapshotWrites = 1;
        var worker = await StartWorkerAsync();
        await WaitUntilAsync(() => _store.SnapshotWriteAttempts == 1);

        var restoredAt = DateTime.UtcNow;
        worker.RedisConnectionRestored();

        await WaitUntilAsync(() => _store.Snapshots.Count == 1);
        Assert.True(DateTime.UtcNow - restoredAt < TimeSpan.FromMilliseconds(600),
            "The retry should not wait out the rest of the 1 second backoff after the connection is restored.");
    }

    // The reconnect arrives while the failing attempt is still running, before the worker reaches its retry wait.
    [Fact]
    public async Task Reconnect_during_the_failing_attempt_ends_the_retry_wait()
    {
        _store.FailingSnapshotWrites = 1;
        var attemptFailedAt = DateTime.MinValue;
        var worker = CreateWorker();
        _store.BeforeFailingWrite = () =>
        {
            worker.RedisConnectionRestored();
            attemptFailedAt = DateTime.UtcNow;
        };

        await worker.StartAsync(CancellationToken.None);

        await WaitUntilAsync(() => _store.Snapshots.Count == 1);
        Assert.True(DateTime.UtcNow - attemptFailedAt < TimeSpan.FromMilliseconds(600),
            "A reconnect during the failed attempt must shorten the 1 second retry wait.");
    }

    // A reconnect signalled before an attempt starts says nothing about that attempt's failure.
    [Fact]
    public async Task Reconnect_from_before_the_attempt_does_not_skip_the_retry_wait()
    {
        var worker = await StartWorkerAsync();
        await WaitUntilAsync(() => _store.Snapshots.Count == 1);
        _store.FailingSnapshotWrites = 1;

        worker.RedisConnectionRestored(); // signalled now; it also requests the rebuild that will fail

        await WaitUntilAsync(() => _store.SnapshotWriteAttempts == 2); // that rebuild failed
        await Task.Delay(400);
        Assert.Equal(2, _store.SnapshotWriteAttempts);                   // still waiting out the 1 second
        await WaitUntilAsync(() => _store.Snapshots.Count == 2);         // then retried
    }
}
