using IncidentMonitoring.Core.Dtos;
using IncidentMonitoring.Core.Interfaces;
using IncidentMonitoring.Core.Models;
using IncidentMonitoring.Core.Rules;
using IncidentMonitoring.Infrastructure.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IncidentMonitoring.Infrastructure.Redis;

/// <summary>
/// The only writer of the Redis dashboard. Each rebuild reads one PostgreSQL snapshot, builds the whole dashboard
/// from it and writes it to Redis with absolute values, so a rebuild can always be repeated safely.
///
/// A rebuild runs at startup, after <see cref="RequestRefresh"/> (many requests are coalesced into one rebuild)
/// and every RefreshIntervalSeconds. A failed rebuild is retried after 1, 2, 4, ... up to 30 seconds; new requests
/// do not shorten that wait, only a restored Redis connection does. After each successful write the SignalR
/// "summaryUpdated" message is sent with the summary of exactly that projection.
/// </summary>
public class RedisProjectionWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<DashboardProjectionSettings> options,
    ILogger<RedisProjectionWorker> logger) : BackgroundService, IDashboardRefresher
{
    private static readonly TimeSpan RetryBaseDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan RetryMaxDelay = TimeSpan.FromSeconds(30);

    private readonly RefreshSignal _refresh = new();
    private readonly SemaphoreSlim _redisReconnected = new(0, 1);

    public void RequestRefresh() => _refresh.Request();

    /// <summary>Called when the Redis connection comes back: rebuild now instead of waiting out the retry delay.</summary>
    public void RedisConnectionRestored()
    {
        // Signal the reconnect before waking the worker: the rebuild this call starts must see the reconnect as
        // older than itself, so if that rebuild fails anyway it still waits out its retry delay.
        try
        {
            _redisReconnected.Release();
        }
        catch (SemaphoreFullException)
        {
            // Already signalled.
        }
        _refresh.Request();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield(); // do not hold up application startup

        // At least 1 second: 0 would rebuild continuously.
        var refreshInterval = TimeSpan.FromSeconds(Math.Max(1, options.Value.RefreshIntervalSeconds));
        var failures = 0;
        _refresh.Request(); // startup rebuild

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                // Wait for a request, or rebuild anyway when the interval passes (the periodic safety net).
                if (!_refresh.IsDirty && !await _refresh.WaitAsync(refreshInterval, stoppingToken))
                    _refresh.MarkDirty();

                // A wake-up whose request an earlier rebuild already covered: nothing to do.
                if (!_refresh.TryClaim())
                    continue;

                // A reconnect signalled before this attempt is not news for it: forget it, so that if the attempt
                // fails, only a reconnect that happens from now on can shorten the retry wait below.
                _redisReconnected.Wait(0);

                try
                {
                    await RebuildAsync();
                    if (failures > 0)
                        logger.LogInformation("Dashboard projection rebuilt again after {Failures} failed attempts", failures);
                    failures = 0;
                }
                catch (Exception ex)
                {
                    // Still out of date. MarkDirty does not wake the loop, so requests arriving during the wait below
                    // only keep the flag set; they cannot turn a Redis or PostgreSQL outage into a busy retry loop.
                    _refresh.MarkDirty();
                    failures++;
                    var delay = RetryBackoff.Delay(failures, RetryBaseDelay, RetryMaxDelay);
                    logger.LogError(ex, "Dashboard projection rebuild failed (attempt {Failures}); retrying in {Delay}", failures, delay);

                    // Ends early only if Redis reconnected after this attempt started (even before this line).
                    await _redisReconnected.WaitAsync(delay, stoppingToken);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The application is shutting down.
        }
    }

    private async Task RebuildAsync()
    {
        // The repository and its DbContext are scoped; this worker lives for the whole application.
        using var scope = scopeFactory.CreateScope();
        var snapshot = await scope.ServiceProvider.GetRequiredService<IEventRepository>().GetDashboardSnapshotAsync();
        var state = DashboardProjection.Build(snapshot);
        await scope.ServiceProvider.GetRequiredService<IDashboardStore>().WriteSnapshotAsync(state);

        logger.LogDebug("Dashboard projection written: {Total} events, {Services} services, snapshot {SnapshotAt:O}",
            state.Counters.Total, state.Services.Count, state.SnapshotAt);

        // The projection is written; from here on nothing can make this rebuild fail.
        await NotifySummaryAsync(scope.ServiceProvider, state);
    }

    /// <summary>
    /// Pushes the summary of exactly the state just written, not a re-read of Redis. Best effort: a failed
    /// notification is logged and does not trigger a rebuild; the next rebuild sends a newer summary anyway.
    /// </summary>
    private async Task NotifySummaryAsync(IServiceProvider services, DashboardState state)
    {
        try
        {
            await services.GetRequiredService<IEventNotifier>().SummaryUpdatedAsync(DashboardSummaryDto.From(state));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Dashboard summary notification failed for snapshot {SnapshotAt:O}", state.SnapshotAt);
        }
    }
}
