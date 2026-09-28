using System.Net.Sockets;
using Npgsql;
using StackExchange.Redis;

namespace IncidentMonitoring.Infrastructure;

/// <summary>
/// Recognizes temporary PostgreSQL infrastructure failures: lost connection, timeout, server restarting.
/// Only these are retried by the Kafka consumer and returned as 503 by the API.
/// Every other exception is treated as a real error of the message or request.
/// </summary>
public static class TransientFailure
{
    public static bool IsTransient(Exception exception)
    {
        // EF Core wraps database errors (e.g. DbUpdateException), so the whole inner chain is checked.
        var chain = new List<Exception>();
        for (Exception? current = exception; current is not null; current = current.InnerException)
            chain.Add(current);

        // A Redis failure is not a database failure: Redis is only a projection and has its own handling. Redis
        // exceptions can wrap the same SocketException or TimeoutException as a database outage, so any Redis
        // exception in the chain rules it out. (RedisTimeoutException derives from TimeoutException, not RedisException.)
        if (chain.Any(e => e is RedisException or RedisTimeoutException))
            return false;

        return chain.Any(e => e
            // Npgsql marks lost connections and server shutdown/restart (SQLSTATE 08xxx, 57P01-57P03, ...) as transient.
            is NpgsqlException { IsTransient: true }
            or TimeoutException
            // Npgsql resolves the host name before it wraps connection errors, so when the database host is gone
            // (e.g. its container is stopped) a bare SocketException "Name or service not known" reaches the caller.
            or SocketException);
    }
}
