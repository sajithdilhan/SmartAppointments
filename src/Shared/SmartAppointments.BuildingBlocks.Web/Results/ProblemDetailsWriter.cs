using Microsoft.AspNetCore.Http;
using SmartAppointments.BuildingBlocks.Models;
using System.Text.Json;

namespace SmartAppointments.BuildingBlocks.Web.Results;

/// <summary>
/// The one place that writes an <see cref="ApiProblemDetails"/> body as <c>application/problem+json</c>,
/// serialized with <see cref="JsonSerializerOptions.Web"/> (camelCase), the same shape MVC writes for a
/// controller's failed <c>Result</c>: <c>{"status":429,"detail":"..."}</c>.
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

        await context.Response.WriteAsync(JsonSerializer.Serialize(problem, JsonSerializerOptions.Web));
    }
}
