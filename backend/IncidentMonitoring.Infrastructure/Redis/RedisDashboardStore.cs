using System.Globalization;
using IncidentMonitoring.Core.Interfaces;
using IncidentMonitoring.Core.Models;
using StackExchange.Redis;

namespace IncidentMonitoring.Infrastructure.Redis;

/// <summary>
/// The dashboard projection in Redis. It is derived from PostgreSQL and rewritten as a whole, with absolute values,
/// by <see cref="RedisProjectionWorker"/> only.
///
///   dashboard:total              STRING  total number of events
///   dashboard:critical           STRING  number of CRITICAL events that are not RESOLVED
///   dashboard:snapshotAt         STRING  PostgreSQL time of the snapshot the projection was built from (UTC, ISO 8601)
///   severity:{SEVERITY}:count    STRING  number of events per severity (all statuses)
///   status:{STATUS}:count        STRING  number of events per status
///   services                     SET     names of all services that have events
///   service:{name}:status        HASH    lastEventTime, latestSeverity, open:{SEVERITY} (unresolved incident counts)
/// </summary>
public class RedisDashboardStore(IConnectionMultiplexer redis) : IDashboardStore
{
    private const string TotalKey = "dashboard:total";
    private const string CriticalKey = "dashboard:critical";
    private const string SnapshotAtKey = "dashboard:snapshotAt";
    private const string ServicesKey = "services";

    // Recent events used to be kept in this list; they are read from PostgreSQL now. Deleted by every rebuild.
    private const string LegacyRecentEventsKey = "recent:events";

    private static string SeverityKey(Severity severity) => $"severity:{severity}:count";
    private static string StatusKey(EventStatus status) => $"status:{status}:count";
    private static string ServiceKey(string service) => $"service:{service}:status";
    private static string OpenField(Severity severity) => $"open:{severity}";

    private IDatabase Db => redis.GetDatabase();

    public async Task WriteSnapshotAsync(DashboardState state)
    {
        var db = Db;

        // Services that are no longer in PostgreSQL must disappear, so the old service keys are deleted. Only the
        // projection worker writes the projection, so this list cannot change before the transaction runs.
        var oldServices = await db.SetMembersAsync(ServicesKey);

        var tx = db.CreateTransaction();
        var deletedKeys = oldServices.Select(s => (RedisKey)ServiceKey(s.ToString()))
            .Append(ServicesKey)
            .Append(LegacyRecentEventsKey)
            .ToArray();
        var commands = new List<Task> { tx.KeyDeleteAsync(deletedKeys) };

        // Every counter is written, zeros included, so no value from an older projection survives.
        var counters = new List<KeyValuePair<RedisKey, RedisValue>>
        {
            new(TotalKey, state.Counters.Total),
            new(CriticalKey, state.CriticalEvents),
            new(SnapshotAtKey, FormatUtc(state.SnapshotAt))
        };
        counters.AddRange(Enum.GetValues<Severity>().Select(s => new KeyValuePair<RedisKey, RedisValue>(SeverityKey(s), state.Counters.BySeverity[s])));
        counters.AddRange(Enum.GetValues<EventStatus>().Select(s => new KeyValuePair<RedisKey, RedisValue>(StatusKey(s), state.Counters.ByStatus[s])));
        commands.Add(tx.StringSetAsync(counters.ToArray()));

        foreach (var service in state.Services)
        {
            var fields = Enum.GetValues<Severity>().Select(s => new HashEntry(OpenField(s), service.OpenBySeverity[s])).ToList();
            if (service.LastEventTime is { } lastEventTime)
                fields.Add(new HashEntry("lastEventTime", FormatUtc(lastEventTime)));
            if (service.LatestSeverity is { } latestSeverity)
                fields.Add(new HashEntry("latestSeverity", latestSeverity.ToString()));
            commands.Add(tx.HashSetAsync(ServiceKey(service.Name), fields.ToArray()));
        }

        if (state.Services.Count > 0)
            commands.Add(tx.SetAddAsync(ServicesKey, state.Services.Select(s => (RedisValue)s.Name).ToArray()));

        // MULTI/EXEC: Redis runs the queued commands one after another without another client's command in between.
        // That does not make REST reads atomic: they use several separate commands, so one read may still see parts
        // of two projection versions (SignalR pushes the exact DashboardState written here instead).
        // There is no rollback: if anything fails, the worker writes the whole projection again later.
        if (!await tx.ExecuteAsync())
            throw new RedisException("The dashboard projection transaction was not executed.");
        await Task.WhenAll(commands);
    }

    public async Task<DashboardTotals> GetCountersAsync()
    {
        var severities = Enum.GetValues<Severity>();
        var statuses = Enum.GetValues<EventStatus>();

        // Read all scalar values with a single MGET: total, critical, snapshotAt, then severities, then statuses.
        var keys = new List<RedisKey> { TotalKey, CriticalKey, SnapshotAtKey };
        keys.AddRange(severities.Select(s => (RedisKey)SeverityKey(s)));
        keys.AddRange(statuses.Select(s => (RedisKey)StatusKey(s)));
        var values = await Db.StringGetAsync(keys.ToArray());

        var total = ToLong(values[0]);
        var critical = ToLong(values[1]);
        DateTime? snapshotAt = values[2].HasValue
            ? DateTime.Parse(values[2].ToString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
            : null; // no projection has been written yet
        var bySeverity = severities.Select((s, i) => (s, ToLong(values[3 + i]))).ToDictionary(x => x.s, x => x.Item2);
        var byStatus = statuses.Select((s, i) => (s, ToLong(values[3 + severities.Length + i]))).ToDictionary(x => x.s, x => x.Item2);

        return new DashboardTotals(new DashboardCounters(total, bySeverity, byStatus), critical, snapshotAt);
    }

    public async Task<List<ServiceState>> GetServicesAsync()
    {
        var names = await Db.SetMembersAsync(ServicesKey);
        var services = new List<ServiceState>();

        foreach (var name in names.Select(n => n.ToString()).Order())
        {
            var fields = (await Db.HashGetAllAsync(ServiceKey(name))).ToDictionary(f => f.Name.ToString(), f => f.Value);

            DateTime? lastEventTime = fields.TryGetValue("lastEventTime", out var time)
                ? DateTime.Parse(time.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
                : null;
            Severity? latestSeverity = fields.TryGetValue("latestSeverity", out var severity)
                ? Enum.Parse<Severity>(severity.ToString())
                : null;
            var openBySeverity = Enum.GetValues<Severity>().ToDictionary(
                s => s,
                s => fields.TryGetValue(OpenField(s), out var count) ? ToLong(count) : 0);

            services.Add(new ServiceState(name, lastEventTime, latestSeverity, openBySeverity));
        }

        return services;
    }

    private static long ToLong(RedisValue value) => value.TryParse(out long result) ? result : 0;

    // All times in this project are UTC; PostgreSQL returns them as DateTimeKind.Utc. An Unspecified value is taken
    // to be UTC as well, so the stored text always ends in "Z" (round-trip "O" format, like lastEventTime).
    private static string FormatUtc(DateTime value) =>
        (value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc))
            .ToString("O", CultureInfo.InvariantCulture);
}
