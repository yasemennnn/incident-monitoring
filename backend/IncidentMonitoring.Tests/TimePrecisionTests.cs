using IncidentMonitoring.Core;

namespace IncidentMonitoring.Tests;

public class TimePrecisionTests
{
    // 2026-06-01T10:15:00.1234567Z: the last digit (700 ns) is finer than a microsecond.
    private static readonly DateTime WithSubMicroseconds =
        new DateTime(2026, 6, 1, 10, 15, 0, DateTimeKind.Utc).AddTicks(1_234_567);

    [Fact]
    public void Sub_microsecond_ticks_are_truncated_not_rounded()
    {
        var result = TimePrecision.ToMicroseconds(WithSubMicroseconds);

        Assert.Equal(new DateTime(2026, 6, 1, 10, 15, 0, DateTimeKind.Utc).AddTicks(1_234_560), result);
    }

    [Fact]
    public void Microsecond_aligned_value_is_unchanged()
    {
        var aligned = new DateTime(2026, 6, 1, 10, 15, 0, DateTimeKind.Utc).AddTicks(1_234_560);

        Assert.Equal(aligned, TimePrecision.ToMicroseconds(aligned));
    }

    [Fact]
    public void Utc_kind_is_preserved()
    {
        Assert.Equal(DateTimeKind.Utc, TimePrecision.ToMicroseconds(WithSubMicroseconds).Kind);
    }

    [Fact]
    public void Date_time_offset_keeps_its_instant_and_offset()
    {
        var value = new DateTimeOffset(WithSubMicroseconds).ToOffset(TimeSpan.FromHours(3));

        var result = TimePrecision.ToMicroseconds(value);

        Assert.Equal(TimeSpan.FromHours(3), result.Offset);
        Assert.Equal(TimePrecision.ToMicroseconds(WithSubMicroseconds), result.UtcDateTime);
    }
}
