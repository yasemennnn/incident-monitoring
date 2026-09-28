using IncidentMonitoring.Infrastructure.Redis;

namespace IncidentMonitoring.Tests;

public class RefreshSignalTests
{
    private static Task<bool> WaitNow(RefreshSignal signal) => signal.WaitAsync(TimeSpan.Zero, CancellationToken.None);

    [Fact]
    public void New_signal_is_clean()
    {
        var signal = new RefreshSignal();

        Assert.False(signal.IsDirty);
        Assert.False(signal.TryClaim());
    }

    [Fact]
    public async Task Many_requests_leave_one_wake_up_and_one_rebuild()
    {
        var signal = new RefreshSignal();

        for (var i = 0; i < 10; i++)
            signal.Request();

        Assert.True(await WaitNow(signal));  // one pending wake-up...
        Assert.False(await WaitNow(signal)); // ...and no queue behind it
        Assert.True(signal.TryClaim());      // one rebuild covers all ten
        Assert.False(signal.TryClaim());
    }

    [Fact]
    public async Task Requests_from_many_threads_are_coalesced()
    {
        var signal = new RefreshSignal();

        await Task.WhenAll(Enumerable.Range(0, 100).Select(_ => Task.Run(signal.Request)));

        Assert.True(await WaitNow(signal));
        Assert.False(await WaitNow(signal));
        Assert.True(signal.TryClaim());
        Assert.False(signal.TryClaim());
    }

    [Fact]
    public async Task Request_after_the_claim_makes_it_dirty_again()
    {
        var signal = new RefreshSignal();
        signal.Request();
        await WaitNow(signal);
        Assert.True(signal.TryClaim()); // the rebuild starts

        signal.Request();               // arrives while the rebuild is running

        Assert.True(signal.IsDirty);
        Assert.True(await WaitNow(signal));
        Assert.True(signal.TryClaim()); // so another rebuild follows
    }

    [Fact]
    public async Task Mark_dirty_does_not_wake_the_worker()
    {
        var signal = new RefreshSignal();

        signal.MarkDirty();

        Assert.True(signal.IsDirty);
        Assert.False(await WaitNow(signal));
    }
}
