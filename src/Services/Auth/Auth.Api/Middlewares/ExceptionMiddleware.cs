using System.Net;
using System.Text.Json;

namespace Auth.Api.Middlewares;

public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;

    public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
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
            _logger.LogError(ex, "Unhandled exception caught in middleware");
            await HandleExceptionAsync(context, ex);
        }
    }

    private static async Task HandleExceptionAsync(HttpContext context, Exception ex)
    {
        context.Response.ContentType = "application/json";

        var statusCode = ex switch
        {
            ArgumentNullException => HttpStatusCode.BadRequest,
            InvalidOperationException => HttpStatusCode.BadRequest,
            UnauthorizedAccessException => HttpStatusCode.Unauthorized,
            _ => HttpStatusCode.InternalServerError
        };

        var message = statusCode switch
        {
            HttpStatusCode.BadRequest => ex.Message,
            HttpStatusCode.NotFound => "Resource not found.",
            HttpStatusCode.Conflict => "Resource conflict.",
            HttpStatusCode.Unauthorized => "Unauthorized access.",
            _ => "An unexpected error occurred! Please try again later."
        };

        var problem = new
        {
            Status = (int)statusCode,
            Detail = message,
            Instance = context.Request.Path
        };

        context.Response.StatusCode = problem.Status;
        context.Response.ContentType = "application/problem+json";

        await context.Response.WriteAsync(JsonSerializer.Serialize(problem));
    }
}
