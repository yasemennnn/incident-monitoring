using System.Globalization;
using IncidentMonitoring.Core.Interfaces;
using IncidentMonitoring.Core.Models;
using IncidentMonitoring.Core.Rules;
using StackExchange.Redis;

namespace IncidentMonitoring.Infrastructure.Redis;

/// <summary>
/// Live dashboard state in Redis.
///
///   dashboard:total              STRING  total number of events
///   severity:{SEVERITY}:count    STRING  number of events per severity
///   status:{STATUS}:count        STRING  number of events per status
///   services                     SET     names of all known services
///   service:{name}:status        HASH    lastEventTime, latestSeverity, open:{SEVERITY} (open incident counts)
///   recent:events                LIST    ids of the latest events, newest first (max 50)
/// </summary>
public class RedisDashboardStore(IConnectionMultiplexer redis) : IDashboardStore
{
    public const int RecentEventsLimit = 50;

    private const string TotalKey = "dashboard:total";
    private const string ServicesKey = "services";
    private const string RecentEventsKey = "recent:events";

    private static string SeverityKey(Severity severity) => $"severity:{severity}:count";
    private static string StatusKey(EventStatus status) => $"status:{status}:count";
    private static string ServiceKey(string service) => $"service:{service}:status";
    private static string OpenField(Severity severity) => $"open:{severity}";

    private IDatabase Db => redis.GetDatabase();

    public async Task AddEventAsync(IncidentEvent e)
    {
        // A transaction (MULTI/EXEC) applies all changes together or none of them.
        var tx = Db.CreateTransaction();

        _ = tx.StringIncrementAsync(TotalKey);
        _ = tx.StringIncrementAsync(SeverityKey(e.Severity));
        _ = tx.StringIncrementAsync(StatusKey(e.Status));

        _ = tx.SetAddAsync(ServicesKey, e.Service);
        // Kafka delivers the events of one service in order (the message key is the service name),
        // so the event being processed is always the latest one for its service.
        _ = tx.HashSetAsync(ServiceKey(e.Service),
        [
            new HashEntry("lastEventTime", e.Timestamp.ToString("O", CultureInfo.InvariantCulture)),
            new HashEntry("latestSeverity", e.Severity.ToString())
        ]);
        if (StatusRules.IsOpen(e.Status))
            _ = tx.HashIncrementAsync(ServiceKey(e.Service), OpenField(e.Severity));

        _ = tx.ListLeftPushAsync(RecentEventsKey, e.EventId);
        _ = tx.ListTrimAsync(RecentEventsKey, 0, RecentEventsLimit - 1);

        await tx.ExecuteAsync();
    }

    public async Task UpdateStatusAsync(IncidentEvent e, EventStatus oldStatus)
    {
        var tx = Db.CreateTransaction();

        _ = tx.StringDecrementAsync(StatusKey(oldStatus));
        _ = tx.StringIncrementAsync(StatusKey(e.Status));

        // The service's open incident count only changes when the event is resolved or reopened.
        var wasOpen = StatusRules.IsOpen(oldStatus);
        var isOpen = StatusRules.IsOpen(e.Status);
        if (wasOpen && !isOpen)
            _ = tx.HashDecrementAsync(ServiceKey(e.Service), OpenField(e.Severity));
        if (!wasOpen && isOpen)
            _ = tx.HashIncrementAsync(ServiceKey(e.Service), OpenField(e.Severity));

        await tx.ExecuteAsync();
    }

    public async Task<DashboardCounters> GetCountersAsync()
    {
        var severities = Enum.GetValues<Severity>();
        var statuses = Enum.GetValues<EventStatus>();

        // Read all counters with a single MGET.
        var keys = new List<RedisKey> { TotalKey };
        keys.AddRange(severities.Select(s => (RedisKey)SeverityKey(s)));
        keys.AddRange(statuses.Select(s => (RedisKey)StatusKey(s)));
        var values = await Db.StringGetAsync(keys.ToArray());

        var total = ToLong(values[0]);
        var bySeverity = severities.Select((s, i) => (s, ToLong(values[1 + i]))).ToDictionary(x => x.s, x => x.Item2);
        var byStatus = statuses.Select((s, i) => (s, ToLong(values[1 + severities.Length + i]))).ToDictionary(x => x.s, x => x.Item2);

        return new DashboardCounters(total, bySeverity, byStatus);
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

    public async Task<List<string>> GetRecentEventIdsAsync(int count)
    {
        var ids = await Db.ListRangeAsync(RecentEventsKey, 0, count - 1);
        return ids.Select(id => id.ToString()).ToList();
    }

    private static long ToLong(RedisValue value) => value.TryParse(out long result) ? result : 0;
}
