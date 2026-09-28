namespace IncidentMonitoring.Infrastructure.Kafka;

/// <summary>Capped exponential backoff: baseDelay, 2 × baseDelay, 4 × baseDelay, … never more than maxDelay.</summary>
public static class RetryBackoff
{
    /// <param name="attempt">1 for the first retry.</param>
    public static TimeSpan Delay(int attempt, TimeSpan baseDelay, TimeSpan maxDelay)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(attempt, 1);

        // Doubling stops at the cap, so a long outage (a large attempt number) cannot overflow.
        var delay = baseDelay;
        for (var i = 1; i < attempt && delay < maxDelay; i++)
            delay *= 2;

        return delay < maxDelay ? delay : maxDelay;
    }
}
