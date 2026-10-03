using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SmartAppointments.BuildingBlocks.Models;
using System.Text.Json;

namespace SmartAppointments.BuildingBlocks.Web.Middlewares;

/// <summary>
/// Turns an exception that escaped the pipeline into <c>application/problem+json</c>. Expected
/// failures travel as <see cref="Result{T}"/> errors, so anything reaching here is unexpected and
/// its message stays in the log: none of the responses below echo it.
/// </summary>
public sealed class ExceptionMiddleware
{
    public const string UnexpectedErrorMessage = "An unexpected error occurred! Please try again later.";
    public const string UnauthorizedMessage = "Unauthorized access.";
    public const string BadRequestMessage = "The request could not be read.";

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
        var problem = ex switch
        {
            UnauthorizedAccessException => new ApiProblemDetails(StatusCodes.Status401Unauthorized, UnauthorizedMessage),
            BadHttpRequestException badRequest => new ApiProblemDetails(badRequest.StatusCode, BadRequestMessage),
            _ => new ApiProblemDetails(StatusCodes.Status500InternalServerError, UnexpectedErrorMessage)
        };

        context.Response.StatusCode = problem.Status;
        context.Response.ContentType = "application/problem+json";

        await context.Response.WriteAsync(JsonSerializer.Serialize(problem));
    }
}
