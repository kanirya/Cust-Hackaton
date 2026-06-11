using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace TaxNetGuardian.Api;

/// <summary>
/// Catches all unhandled exceptions and returns RFC 7807 Problem Details responses.
/// Prevents raw stack traces leaking to callers in production.
/// </summary>
public sealed class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;

    public GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var correlationId = context.TraceIdentifier;

        var (statusCode, title) = exception switch
        {
            InvalidOperationException => (StatusCodes.Status400BadRequest, "Bad Request"),
            ArgumentException => (StatusCodes.Status400BadRequest, "Bad Request"),
            UnauthorizedAccessException => (StatusCodes.Status401Unauthorized, "Unauthorized"),
            KeyNotFoundException => (StatusCodes.Status404NotFound, "Not Found"),
            OperationCanceledException => (499, "Client Closed Request"),
            _ => (StatusCodes.Status500InternalServerError, "Internal Server Error")
        };

        if (statusCode >= 500)
        {
            _logger.LogError(exception,
                "Unhandled exception. Method={Method} Path={Path} CorrelationId={CorrelationId}",
                context.Request.Method,
                context.Request.Path,
                correlationId);
        }
        else
        {
            _logger.LogWarning(
                "Handled exception {ExceptionType}. Method={Method} Path={Path} CorrelationId={CorrelationId} Message={Message}",
                exception.GetType().Name,
                context.Request.Method,
                context.Request.Path,
                correlationId,
                exception.Message);
        }

        var problem = new ProblemDetails
        {
            Type = $"https://taxnet.gov.pk/errors/{title.ToLowerInvariant().Replace(' ', '-')}",
            Title = title,
            Status = statusCode,
            Detail = statusCode < 500
                ? exception.Message
                : "An unexpected error occurred. Contact support with the correlationId.",
            Instance = context.Request.Path
        };

        problem.Extensions["correlationId"] = correlationId;
        problem.Extensions["timestamp"] = DateTimeOffset.UtcNow;

        if (!context.Response.HasStarted)
        {
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/problem+json";

            await context.Response.WriteAsync(JsonSerializer.Serialize(problem, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));
        }
    }
}
