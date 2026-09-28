using IncidentMonitoring.Core.Models;
using IncidentMonitoring.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace IncidentMonitoring.IntegrationTests;

// What these tests prove is what the in-memory fakes cannot: PostgreSQL's real error for a duplicate key, real row
// locking between two concurrent updates, and that the dashboard snapshot queries run and return the right rows.
public class EventRepositoryTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private static readonly DateTime T0 = new(2026, 9, 28, 10, 0, 0, DateTimeKind.Utc);

    // Every test starts with an empty table. The tests of one class run one after another, so they never overlap.
    public async Task InitializeAsync()
    {
        await using var db = postgres.NewContext();
        await db.Database.ExecuteSqlRawAsync("TRUNCATE events");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Duplicate_event_id_is_rejected_by_the_primary_key_and_the_context_stays_usable()
    {
        const string eventId = "EVT-3F2A9C1E7B4D4E8FA6C0D5B1E2F3A4B5";
        // .123456 s: PostgreSQL keeps microseconds, so this value must come back unchanged.
        var timestamp = T0.AddTicks(1_234_560);

        await using (var first = postgres.NewContext())
            Assert.True(await new EventRepository(first).AddAsync(Event(eventId, "route-service", message: "Original", timestamp: timestamp)));

        // The redelivered message is handled in a new scope, and after the failed insert EventProcessor reads the
        // stored row through the same repository (and DbContext) to classify it as Duplicate or Conflict.
        await using var scope = postgres.NewContext();
        var repository = new EventRepository(scope);

        Assert.False(await repository.AddAsync(Event(eventId, "route-service", message: "Different content")));

        var stored = await repository.GetAsync(eventId);
        Assert.NotNull(stored);
        Assert.Equal("Original", stored.Message);
        Assert.Equal(timestamp, stored.Timestamp);
        Assert.Equal(1, await CountEventsAsync());
    }

    [Fact]
    public async Task Of_two_concurrent_conditional_updates_only_the_first_committed_one_wins()
    {
        const string eventId = "EVT-7C1D2E3F4A5B4C6D8E9F0A1B2C3D4E5F";
        var winnerTime = T0.AddMinutes(1);
        await SeedAsync(Event(eventId, "route-service", EventStatus.OPEN));

        await using var winnerDb = postgres.NewContext();
        await using var loserDb = postgres.NewContext();

        // The winner updates inside an open transaction, so PostgreSQL keeps the row locked until the commit.
        await using var transaction = await winnerDb.Database.BeginTransactionAsync();
        Assert.True(await new EventRepository(winnerDb).TryUpdateStatusAsync(eventId, EventStatus.OPEN, EventStatus.ACKNOWLEDGED, winnerTime));

        // Same expected status (OPEN), different target. This UPDATE must wait for the winner's row lock.
        var loser = new EventRepository(loserDb).TryUpdateStatusAsync(eventId, EventStatus.OPEN, EventStatus.RESOLVED, T0.AddMinutes(2));
        await WaitUntilAnUpdateWaitsForARowLockAsync(loser);

        // After the commit PostgreSQL re-checks the waiting UPDATE's WHERE clause against the new row:
        // Status is no longer OPEN, so it changes nothing.
        await transaction.CommitAsync();
        Assert.False(await loser);

        await using var check = postgres.NewContext();
        var stored = await new EventRepository(check).GetAsync(eventId);
        Assert.NotNull(stored);
        Assert.Equal(EventStatus.ACKNOWLEDGED, stored.Status);
        Assert.Equal(winnerTime, stored.StatusUpdatedAt);
    }

    [Fact]
    public async Task Dashboard_snapshot_returns_grouped_counts_and_the_latest_event_per_service()
    {
        var latest = T0.AddMinutes(10);
        await SeedAsync(
            Event("EVT-0A000000000040008000000000000001", "route-service", EventStatus.OPEN, Severity.CRITICAL, T0.AddMinutes(1)),
            Event("EVT-0A000000000040008000000000000002", "route-service", EventStatus.OPEN, Severity.CRITICAL, T0.AddMinutes(2)),
            Event("EVT-0A000000000040008000000000000003", "route-service", EventStatus.ACKNOWLEDGED, Severity.CRITICAL, T0.AddMinutes(3)),
            Event("EVT-0A000000000040008000000000000004", "route-service", EventStatus.RESOLVED, Severity.CRITICAL, T0.AddMinutes(4)),
            // Two latest events with exactly the same timestamp: EventId decides, the higher one is the latest.
            Event("EVT-1B000000000040008000000000000005", "route-service", EventStatus.OPEN, Severity.INFO, latest),
            Event("EVT-AB000000000040008000000000000006", "route-service", EventStatus.OPEN, Severity.MAJOR, latest),
            // A service whose events are all resolved still appears.
            Event("EVT-0C000000000040008000000000000007", "power-supply-service", EventStatus.RESOLVED, Severity.WARNING, T0.AddMinutes(5)),
            Event("EVT-0C000000000040008000000000000008", "power-supply-service", EventStatus.RESOLVED, Severity.WARNING, T0.AddMinutes(6)));

        var before = await DatabaseTimeAsync();
        DashboardSnapshot snapshot;
        await using (var db = postgres.NewContext())
            snapshot = await new EventRepository(db).GetDashboardSnapshotAsync();
        var after = await DatabaseTimeAsync();

        var expectedCounts = new[]
        {
            new EventCountRow("power-supply-service", EventStatus.RESOLVED, Severity.WARNING, 2),
            new EventCountRow("route-service", EventStatus.OPEN, Severity.INFO, 1),
            new EventCountRow("route-service", EventStatus.OPEN, Severity.MAJOR, 1),
            new EventCountRow("route-service", EventStatus.OPEN, Severity.CRITICAL, 2),
            new EventCountRow("route-service", EventStatus.ACKNOWLEDGED, Severity.CRITICAL, 1),
            new EventCountRow("route-service", EventStatus.RESOLVED, Severity.CRITICAL, 1)
        };
        Assert.Equal(expectedCounts.OrderBy(Key), snapshot.Counts.OrderBy(Key));

        var expectedLatest = new[]
        {
            new LatestServiceEvent("power-supply-service", T0.AddMinutes(6), Severity.WARNING),
            new LatestServiceEvent("route-service", latest, Severity.MAJOR)   // EVT-AB… beats EVT-1B…
        };
        Assert.Equal(expectedLatest, snapshot.Latest.OrderBy(l => l.Service, StringComparer.Ordinal));

        // The snapshot time comes from PostgreSQL (both bounds are read from the same database clock).
        Assert.Equal(DateTimeKind.Utc, snapshot.SnapshotAt.Kind);
        Assert.InRange(snapshot.SnapshotAt, before, after);
    }

    private static string Key(EventCountRow row) => $"{row.Service}|{row.Status}|{row.Severity}";

    private static IncidentEvent Event(
        string eventId,
        string service,
        EventStatus status = EventStatus.OPEN,
        Severity severity = Severity.INFO,
        DateTime? timestamp = null,
        string message = "Integration test event") => new()
    {
        EventId = eventId,
        Source = "ATS",
        Service = service,
        Severity = severity,
        Message = message,
        Status = status,
        Timestamp = timestamp ?? T0,
        ReceivedAt = T0
    };

    private async Task SeedAsync(params IncidentEvent[] events)
    {
        await using var db = postgres.NewContext();
        db.Events.AddRange(events);
        await db.SaveChangesAsync();
    }

    private async Task<int> CountEventsAsync()
    {
        await using var db = postgres.NewContext();
        return await db.Events.CountAsync();
    }

    private async Task<DateTime> DatabaseTimeAsync()
    {
        await using var db = postgres.NewContext();
        return await db.Database.SqlQueryRaw<DateTime>("SELECT clock_timestamp() AS \"Value\"").SingleAsync();
    }

    /// <summary>
    /// Waits until PostgreSQL itself reports that another session is blocked on a lock, i.e. the competing UPDATE
    /// has really started and is waiting for the winner. A fixed delay would only guess at that; this checks it.
    /// The time limit is a safety net so a broken test fails instead of hanging; the wait is normally milliseconds.
    /// </summary>
    private async Task WaitUntilAnUpdateWaitsForARowLockAsync(Task<bool> update)
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock'",
            connection);

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while ((long)(await command.ExecuteScalarAsync())! == 0)
        {
            if (update.IsCompleted)
                Assert.Fail($"The competing update finished without waiting for the lock (result: {await update}).");
            if (DateTime.UtcNow > deadline)
                Assert.Fail("The competing update did not start waiting for the row lock within 10 seconds.");
        }
    }
}
