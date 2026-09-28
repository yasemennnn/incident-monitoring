using System.Data;
using IncidentMonitoring.Core.Dtos;
using IncidentMonitoring.Core.Interfaces;
using IncidentMonitoring.Core.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace IncidentMonitoring.Infrastructure.Data;

public class EventRepository(AppDbContext db) : IEventRepository
{
    public async Task<bool> AddAsync(IncidentEvent incidentEvent)
    {
        db.Events.Add(incidentEvent);
        try
        {
            await db.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // An event with this id already exists (e.g. the same Kafka message was delivered twice).
            db.Entry(incidentEvent).State = EntityState.Detached;
            return false;
        }
    }

    public Task<IncidentEvent?> GetAsync(string eventId) =>
        db.Events.AsNoTracking().FirstOrDefaultAsync(e => e.EventId == eventId);

    public async Task<bool> TryUpdateStatusAsync(string eventId, EventStatus expectedStatus, EventStatus newStatus, DateTime statusUpdatedAt)
    {
        // "AND Status = expectedStatus" makes concurrent changes safe: a second UPDATE waits for the first one's row
        // lock, PostgreSQL then re-checks the WHERE clause against the new row, and it no longer matches.
        var updatedRows = await db.Events
            .Where(e => e.EventId == eventId && e.Status == expectedStatus)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(e => e.Status, newStatus)
                .SetProperty(e => e.StatusUpdatedAt, (DateTime?)statusUpdatedAt));

        return updatedRows == 1;
    }

    public Task<List<IncidentEvent>> GetRecentAsync(int count) =>
        db.Events.AsNoTracking()
            .OrderByDescending(e => e.Timestamp)
            .ThenByDescending(e => e.EventId) // same order as the event list, stable for equal timestamps
            .Take(count)
            .ToListAsync();

    public async Task<PagedResult<IncidentEvent>> SearchAsync(EventFilter filter)
    {
        var query = db.Events.AsNoTracking();

        if (filter.Severity is not null)
            query = query.Where(e => e.Severity == filter.Severity);
        if (filter.Status is not null)
            query = query.Where(e => e.Status == filter.Status);
        if (!string.IsNullOrWhiteSpace(filter.Source))
            query = query.Where(e => e.Source == filter.Source);
        if (!string.IsNullOrWhiteSpace(filter.Service))
            query = query.Where(e => e.Service == filter.Service);
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var pattern = $"%{filter.Search.Trim()}%";
            query = query.Where(e =>
                EF.Functions.ILike(e.EventId, pattern) ||
                EF.Functions.ILike(e.Message, pattern) ||
                EF.Functions.ILike(e.Service, pattern));
        }

        var totalCount = await query.CountAsync();
        var items = await query
            .OrderByDescending(e => e.Timestamp)
            .ThenByDescending(e => e.EventId) // events with the same timestamp keep a stable order across pages
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync();

        return new PagedResult<IncidentEvent>(items, filter.Page, filter.PageSize, totalCount);
    }

    public async Task<DashboardSnapshot> GetDashboardSnapshotAsync()
    {
        // REPEATABLE READ: every query in this transaction sees the same snapshot, so the counts and the latest
        // events describe the same moment even while new events are being committed. READ ONLY is a safety guard;
        // it must be the first statement of the transaction.
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY");

        // The first query of the transaction establishes the snapshot, so its start time (statement_timestamp())
        // is the snapshot time. It comes from PostgreSQL, not the API server's clock.
        var snapshotAt = await db.Database.SqlQueryRaw<DateTime>("SELECT statement_timestamp() AS \"Value\"").SingleAsync();

        // Q1: one row per (service, status, severity) combination; the events themselves are not loaded.
        var counts = await db.Events
            .GroupBy(e => new { e.Service, e.Status, e.Severity })
            .Select(g => new EventCountRow(g.Key.Service, g.Key.Status, g.Key.Severity, g.LongCount()))
            .ToListAsync();

        // Q2: the latest event of each service. EventId breaks ties between events with the same timestamp.
        // The projection must stay inside the group: EF Core 8 cannot translate a Select placed after First().
        var latest = await db.Events
            .GroupBy(e => e.Service)
            .Select(g => g
                .OrderByDescending(e => e.Timestamp)
                .ThenByDescending(e => e.EventId)
                .Select(e => new LatestServiceEvent(e.Service, e.Timestamp, e.Severity))
                .First())
            .ToListAsync();

        await transaction.CommitAsync();
        return new DashboardSnapshot(counts, latest, snapshotAt);
    }

    public async Task<EventFacetsDto> GetFacetsAsync()
    {
        var sources = await db.Events.Select(e => e.Source).Distinct().OrderBy(s => s).ToListAsync();
        var services = await db.Events.Select(e => e.Service).Distinct().OrderBy(s => s).ToListAsync();
        return new EventFacetsDto(sources, services, Enum.GetValues<Severity>().ToList(), Enum.GetValues<EventStatus>().ToList());
    }
}
