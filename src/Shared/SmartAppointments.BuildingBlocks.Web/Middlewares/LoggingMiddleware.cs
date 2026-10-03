using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace SmartAppointments.BuildingBlocks.Web.Middlewares;

public sealed class LoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<LoggingMiddleware> _logger;

    public LoggingMiddleware(RequestDelegate next, ILogger<LoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        _logger.LogInformation("Incoming request: {Method} {Path}", context.Request.Method, context.Request.Path);
        await _next(context);
    }
}
