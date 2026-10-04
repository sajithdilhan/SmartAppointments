using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Net.Http.Headers;
using SmartAppointments.BuildingBlocks;

namespace SmartAppointments.Gateway.Cors;

/// <summary>
/// The one CORS policy of the gateway, applied by the framework's CORS middleware. The values are fixed
/// here rather than configured; only the allowed origins come from configuration.
/// </summary>
public static class GatewayCors
{
    public const string PolicyName = "gateway-cors";
    public const string IdempotencyKeyHeader = "Idempotency-Key";

    public static readonly string[] AllowedMethods = ["GET", "POST", "PUT", "DELETE"];

    public static readonly string[] AllowedHeaders =
        [HeaderNames.Authorization, HeaderNames.ContentType, IdempotencyKeyHeader, Constants.CorrelationIdHeaderName];

    public static readonly string[] ExposedHeaders =
        [Constants.CorrelationIdHeaderName, HeaderNames.RetryAfter, HeaderNames.Location];

    public static readonly TimeSpan PreflightMaxAge = TimeSpan.FromMinutes(10);

    public static CorsPolicy BuildPolicy(IReadOnlyList<string> origins)
    {
        // An exact ordinal match: no URL normalisation, so `null` or a look-alike host never matches. A
        // delegate rather than WithOrigins, which would omit `Vary: Origin` for a single origin.
        var allowed = new HashSet<string>(origins, StringComparer.Ordinal);

        return new CorsPolicyBuilder()
            .SetIsOriginAllowed(origin => allowed.Contains(origin))
            .WithMethods(AllowedMethods)
            .WithHeaders(AllowedHeaders)
            .WithExposedHeaders(ExposedHeaders)
            .SetPreflightMaxAge(PreflightMaxAge)
            .Build();
    }

    public static IServiceCollection AddGatewayCors(this IServiceCollection services, IReadOnlyList<string> origins)
    {
        var policy = BuildPolicy(origins);
        services.AddCors(options => options.AddPolicy(PolicyName, policy));
        services.AddSingleton(policy);
        return services;
    }

    public static IApplicationBuilder UseGatewayCors(this IApplicationBuilder app) => app.UseCors(PolicyName);
}
