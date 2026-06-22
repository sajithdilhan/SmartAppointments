namespace Auth.Api.Middlewares;

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
        var request = context.Request;
        var method = request.Method;
        var path = request.Path;
        _logger.LogInformation("Incoming request: {Method} {Path}", method, path);
        await _next(context);
    }
}
