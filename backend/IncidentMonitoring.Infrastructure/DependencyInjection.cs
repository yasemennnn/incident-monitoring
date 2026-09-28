using IncidentMonitoring.Core.Interfaces;
using IncidentMonitoring.Infrastructure.Data;
using IncidentMonitoring.Infrastructure.Kafka;
using IncidentMonitoring.Infrastructure.Redis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace IncidentMonitoring.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // PostgreSQL
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(configuration.GetConnectionString("Postgres")));
        services.AddScoped<IEventRepository, EventRepository>();

        // Redis. AbortOnConnectFail = false: the API starts even if Redis is down and reconnects automatically.
        // FailFast: while disconnected, commands fail at once instead of queueing; the projection worker retries.
        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var redisOptions = ConfigurationOptions.Parse(configuration.GetConnectionString("Redis")!);
            redisOptions.AbortOnConnectFail = false;
            redisOptions.BacklogPolicy = BacklogPolicy.FailFast;
            var redis = ConnectionMultiplexer.Connect(redisOptions);

            // The worker is resolved only when the event fires, so creating the connection does not depend on it.
            // The subscription lives exactly as long as this singleton connection.
            redis.ConnectionRestored += (_, _) => sp.GetRequiredService<RedisProjectionWorker>().RedisConnectionRestored();
            return redis;
        });
        services.AddSingleton<IDashboardStore, RedisDashboardStore>();

        // Dashboard projection: the only writer of the Redis dashboard. All three registrations resolve the same
        // singleton (AddHostedService<RedisProjectionWorker>() would create a second instance).
        services.Configure<DashboardProjectionSettings>(configuration.GetSection("DashboardProjection"));
        services.AddSingleton<RedisProjectionWorker>();
        services.AddSingleton<IDashboardRefresher>(sp => sp.GetRequiredService<RedisProjectionWorker>());
        services.AddHostedService(sp => sp.GetRequiredService<RedisProjectionWorker>());

        // Kafka
        services.Configure<KafkaSettings>(configuration.GetSection("Kafka"));
        services.AddHostedService<KafkaConsumerService>();

        services.AddHealthChecks()
            .AddDbContextCheck<AppDbContext>("postgres")
            .AddCheck<RedisHealthCheck>("redis")
            .AddCheck<KafkaHealthCheck>("kafka");

        return services;
    }
}
