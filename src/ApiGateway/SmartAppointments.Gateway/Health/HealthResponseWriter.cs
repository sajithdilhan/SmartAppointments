using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace SmartAppointments.Gateway.Health;

/// <summary>Writes a health report as JSON: overall status and one entry per check, statuses as names.</summary>
public static class HealthResponseWriter
{
    public static async Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";

        var body = new
        {
            status = report.Status.ToString(),
            totalDurationMs = (long)report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Status == HealthStatus.Healthy ? null : e.Value.Description,
                durationMs = (long)e.Value.Duration.TotalMilliseconds
            })
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(body));
    }
}
