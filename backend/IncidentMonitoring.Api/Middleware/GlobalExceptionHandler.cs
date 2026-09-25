using IncidentMonitoring.Core;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using StackExchange.Redis;

namespace IncidentMonitoring.Api.Middleware;

/// <summary>Turns exceptions into consistent ProblemDetails (RFC 7807) responses.</summary>
public class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title) = exception switch
        {
            NotFoundException => (StatusCodes.Status404NotFound, "Not found"),
            InvalidStatusTransitionException => (StatusCodes.Status409Conflict, "Invalid status change"),
            RedisException or RedisTimeoutException => (StatusCodes.Status503ServiceUnavailable, "Live dashboard data (Redis) is unavailable"),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred")
        };

        if (status >= 500)
            logger.LogError(exception, "Request {Method} {Path} failed", context.Request.Method, context.Request.Path);

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            // Messages of our own exceptions are safe to show; internal errors are not.
            Detail = status < 500 ? exception.Message : null,
            Instance = context.Request.Path
        };

        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json", cancellationToken);
        return true;
    }
}
