using IncidentMonitoring.Infrastructure.Kafka;

namespace IncidentMonitoring.Tests;

public class RetryBackoffTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(4, 8)]
    [InlineData(5, 16)]
    [InlineData(6, 30)]
    [InlineData(7, 30)]
    [InlineData(1000, 30)]
    public void Delay_doubles_until_the_cap(int attempt, int expectedSeconds)
    {
        var delay = RetryBackoff.Delay(attempt, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30));

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), delay);
    }
}
