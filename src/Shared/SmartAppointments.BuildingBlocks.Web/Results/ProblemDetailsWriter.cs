using Microsoft.AspNetCore.Http;
using SmartAppointments.BuildingBlocks.Models;
using System.Text.Json;

namespace SmartAppointments.BuildingBlocks.Web.Results;

/// <summary>
/// The one place that writes an <see cref="ApiProblemDetails"/> body as <c>application/problem+json</c>,
/// serialized with the <c>System.Text.Json</c> defaults every service already uses.
/// </summary>
public static class ProblemDetailsWriter
{
    public const string ContentType = "application/problem+json";

    public static Task WriteAsync(HttpContext context, int status, string detail)
        => WriteAsync(context, new ApiProblemDetails(status, detail));

    public static async Task WriteAsync(HttpContext context, ApiProblemDetails problem)
    {
        context.Response.StatusCode = problem.Status;
        context.Response.ContentType = ContentType;

        await context.Response.WriteAsync(JsonSerializer.Serialize(problem));
    }
}
