using IncidentMonitoring.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace IncidentMonitoring.IntegrationTests;

/// <summary>
/// One real PostgreSQL for the whole test class: the same image as docker-compose, with the schema created by the
/// application's own EF Core migrations. It is a separate container; the docker-compose database is never touched.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public DbContextOptions<AppDbContext> Options { get; private set; } = null!;

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        Options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(ConnectionString).Options;

        // The same call the API makes at startup (Program.cs: Database.Migrate()).
        await using var db = NewContext();
        await db.Database.MigrateAsync();
    }

    /// <summary>A new context, like the new DI scope the application creates per Kafka message or HTTP request.</summary>
    public AppDbContext NewContext() => new(Options);

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}
