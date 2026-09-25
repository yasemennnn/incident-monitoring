using IncidentMonitoring.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace IncidentMonitoring.Infrastructure.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<IncidentEvent> Events => Set<IncidentEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<IncidentEvent>();
        e.ToTable("events");

        // The primary key on EventId is what prevents duplicate events.
        e.HasKey(x => x.EventId);

        e.Property(x => x.EventId).HasMaxLength(64);
        e.Property(x => x.Source).HasMaxLength(100);
        e.Property(x => x.Service).HasMaxLength(100);
        e.Property(x => x.Message).HasMaxLength(1000);
        e.Property(x => x.Severity).HasConversion<string>().HasMaxLength(20);
        e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);

        // Indexes for the default sort order and the list filters.
        e.HasIndex(x => x.Timestamp);
        e.HasIndex(x => x.Severity);
        e.HasIndex(x => x.Status);
        e.HasIndex(x => x.Source);
        e.HasIndex(x => x.Service);
    }
}
