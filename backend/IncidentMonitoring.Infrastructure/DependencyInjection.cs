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
        services.AddSingleton<IConnectionMultiplexer>(_ =>
        {
            var redisOptions = ConfigurationOptions.Parse(configuration.GetConnectionString("Redis")!);
            redisOptions.AbortOnConnectFail = false;
            return ConnectionMultiplexer.Connect(redisOptions);
        });
        services.AddSingleton<IDashboardStore, RedisDashboardStore>();

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
