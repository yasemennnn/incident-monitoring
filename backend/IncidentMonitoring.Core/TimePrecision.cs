namespace IncidentMonitoring.Core;

/// <summary>
/// PostgreSQL timestamptz keeps microseconds, while .NET keeps 100 ns ticks. Truncating to microseconds
/// before storing or comparing makes an in-memory value equal to the one read back from the database.
/// </summary>
public static class TimePrecision
{
    public static DateTime ToMicroseconds(DateTime value) =>
        value.AddTicks(-(value.Ticks % TimeSpan.TicksPerMicrosecond));

    public static DateTimeOffset ToMicroseconds(DateTimeOffset value) =>
        value.AddTicks(-(value.Ticks % TimeSpan.TicksPerMicrosecond));
}
