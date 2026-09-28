using System.Net.Sockets;
using Confluent.Kafka;
using IncidentMonitoring.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using StackExchange.Redis;

namespace IncidentMonitoring.Tests;

public class TransientFailureTests
{
    [Fact]
    public void Transient_npgsql_exception_is_transient()
    {
        // Npgsql marks a failure transient when the cause is a network (IO) or timeout problem.
        var exception = new NpgsqlException("connection lost", new IOException("socket closed"));

        Assert.True(TransientFailure.IsTransient(exception));
    }

    [Fact]
    public void Timeout_is_transient()
    {
        Assert.True(TransientFailure.IsTransient(new TimeoutException()));
    }

    [Fact]
    public void Transient_failure_wrapped_in_another_exception_is_transient()
    {
        var exception = new InvalidOperationException("outer", new TimeoutException());

        Assert.True(TransientFailure.IsTransient(exception));
    }

    [Fact]
    public void Db_update_exception_with_transient_inner_exception_is_transient()
    {
        var exception = new DbUpdateException("save failed", new NpgsqlException("connection lost", new IOException()));

        Assert.True(TransientFailure.IsTransient(exception));
    }

    [Fact]
    public void Application_error_is_not_transient()
    {
        Assert.False(TransientFailure.IsTransient(new InvalidOperationException("bug")));
    }

    [Fact]
    public void Db_update_exception_without_transient_inner_exception_is_not_transient()
    {
        var exception = new DbUpdateException("save failed", new NpgsqlException("syntax error"));

        Assert.False(TransientFailure.IsTransient(exception));
    }

    // The shape observed when the PostgreSQL container was stopped: Npgsql's host-name lookup fails before Npgsql
    // wraps connection errors, so the caller gets a bare SocketException.
    [Fact]
    public void Bare_socket_exception_from_an_unreachable_database_host_is_transient()
    {
        var exception = new SocketException((int)SocketError.HostNotFound); // "Name or service not known"

        Assert.True(TransientFailure.IsTransient(exception));
    }

    [Fact]
    public void Socket_exception_wrapped_by_ef_core_is_transient()
    {
        var exception = new DbUpdateException("save failed", new SocketException((int)SocketError.ConnectionRefused));

        Assert.True(TransientFailure.IsTransient(exception));
    }

    // PostgresException is an NpgsqlException; Npgsql itself reports these shutdown/startup states as transient.
    [Theory]
    [InlineData("57P01")] // admin_shutdown: the server is being stopped
    [InlineData("57P02")] // crash_shutdown: another server process crashed
    [InlineData("57P03")] // cannot_connect_now: the server is starting up or shutting down
    public void Postgres_shutdown_and_startup_states_are_transient(string sqlState)
    {
        var exception = new PostgresException("terminating connection", "FATAL", "FATAL", sqlState);

        Assert.True(TransientFailure.IsTransient(exception));
    }

    [Theory]
    [InlineData("23505")] // unique_violation
    [InlineData("42601")] // syntax_error
    public void Postgres_errors_caused_by_the_request_are_not_transient(string sqlState)
    {
        var exception = new PostgresException("error", "ERROR", "ERROR", sqlState);

        Assert.False(TransientFailure.IsTransient(exception));
    }

    // A Redis outage wraps the same low-level exceptions as a database outage; it must not be mistaken for one.
    [Fact]
    public void Redis_connection_failure_wrapping_a_socket_exception_is_not_a_database_failure()
    {
        var exception = new RedisConnectionException(ConnectionFailureType.UnableToConnect, "redis down",
            new SocketException((int)SocketError.ConnectionRefused));

        Assert.False(TransientFailure.IsTransient(exception));
    }

    [Fact]
    public void Redis_exception_wrapping_a_timeout_is_not_a_database_failure()
    {
        Assert.False(TransientFailure.IsTransient(new RedisException("redis timeout", new TimeoutException())));
    }

    [Fact]
    public void Redis_failure_inside_another_exception_is_not_a_database_failure()
    {
        var exception = new InvalidOperationException("outer", new RedisConnectionException(
            ConnectionFailureType.SocketFailure, "redis down", new SocketException((int)SocketError.ConnectionReset)));

        Assert.False(TransientFailure.IsTransient(exception));
    }

    [Fact]
    public void Redis_timeout_is_not_a_database_failure()
    {
        Assert.False(TransientFailure.IsTransient(new RedisTimeoutException("timeout", CommandStatus.Sent)));
    }

    // A failed dead-letter publish must not enter the consumer's unlimited PostgreSQL retry path.
    [Fact]
    public void Kafka_delivery_timeout_is_not_a_database_failure()
    {
        var exception = new ProduceException<string?, string?>(
            new Error(ErrorCode.Local_MsgTimedOut), new DeliveryResult<string?, string?>());

        Assert.False(TransientFailure.IsTransient(exception));
    }
}
