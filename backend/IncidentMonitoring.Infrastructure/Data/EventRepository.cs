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
        db.Events.FirstOrDefaultAsync(e => e.EventId == eventId);

    public Task<List<IncidentEvent>> GetByIdsAsync(List<string> eventIds) =>
        db.Events.AsNoTracking().Where(e => eventIds.Contains(e.EventId)).ToListAsync();

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
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync();

        return new PagedResult<IncidentEvent>(items, filter.Page, filter.PageSize, totalCount);
    }

    public async Task<EventFacetsDto> GetFacetsAsync()
    {
        var sources = await db.Events.Select(e => e.Source).Distinct().OrderBy(s => s).ToListAsync();
        var services = await db.Events.Select(e => e.Service).Distinct().OrderBy(s => s).ToListAsync();
        return new EventFacetsDto(sources, services, Enum.GetValues<Severity>().ToList(), Enum.GetValues<EventStatus>().ToList());
    }

    public Task SaveChangesAsync() => db.SaveChangesAsync();
}
