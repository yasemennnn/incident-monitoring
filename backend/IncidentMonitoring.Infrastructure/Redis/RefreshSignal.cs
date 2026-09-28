namespace IncidentMonitoring.Infrastructure.Redis;

/// <summary>
/// "The dashboard is out of date" flag plus a wake-up for the worker waiting on it.
/// Requests are coalesced: however many arrive, at most one wake-up is pending and one rebuild covers them all.
/// Safe to use from any thread.
/// </summary>
public sealed class RefreshSignal
{
    private int _dirty;                                  // 1 = a rebuild is needed
    private readonly SemaphoreSlim _wake = new(0, 1);    // at most one pending wake-up, so nothing queues up

    public bool IsDirty => Volatile.Read(ref _dirty) == 1;

    /// <summary>Marks the dashboard out of date and wakes the worker. Never blocks.</summary>
    public void Request()
    {
        Interlocked.Exchange(ref _dirty, 1);
        try
        {
            _wake.Release();
        }
        catch (SemaphoreFullException)
        {
            // A wake-up is already pending; it covers this request too.
        }
    }

    /// <summary>Marks the dashboard out of date without waking the worker (used while it waits to retry).</summary>
    public void MarkDirty() => Interlocked.Exchange(ref _dirty, 1);

    /// <summary>
    /// Takes the pending work: returns true and clears the flag if a rebuild is needed. The worker calls this
    /// BEFORE reading PostgreSQL, so a request that arrives during the rebuild sets the flag again and is not lost.
    /// </summary>
    public bool TryClaim() => Interlocked.Exchange(ref _dirty, 0) == 1;

    /// <summary>Waits for a request. Returns false when the timeout passes first.</summary>
    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
        _wake.WaitAsync(timeout, cancellationToken);
}
