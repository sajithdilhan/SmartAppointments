using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace SmartAppointments.Gateway.Health;

public static class HealthExtensions
{
    private static readonly string[] Downstreams = ["auth", "availability", "booking"];

    public static IServiceCollection AddGatewayHealthChecks(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpClient(DownstreamHealthCheck.ClientName, c => c.Timeout = DownstreamHealthCheck.Timeout);

        var checks = services.AddHealthChecks().AddCheck("gateway", () => HealthCheckResult.Healthy());

        foreach (var name in Downstreams)
        {
            // The first destination of the cluster the proxy itself uses (already validated at startup).
            var address = configuration.GetSection($"ReverseProxy:Clusters:{name}:Destinations").GetChildren().First()["Address"]!;
            var url = new Uri(new Uri(address.TrimEnd('/') + "/"), "healthz");

            checks.Add(new HealthCheckRegistration(
                name,
                sp => new DownstreamHealthCheck(url, sp.GetRequiredService<IHttpClientFactory>()),
                HealthStatus.Degraded,
                tags: null));
        }

        return services;
    }

    public static HealthCheckOptions GatewayHealthOptions() => new()
    {
        ResultStatusCodes =
        {
            [HealthStatus.Healthy] = StatusCodes.Status200OK,
            [HealthStatus.Degraded] = StatusCodes.Status200OK,
            [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
        },
        ResponseWriter = HealthResponseWriter.WriteAsync
    };
}
