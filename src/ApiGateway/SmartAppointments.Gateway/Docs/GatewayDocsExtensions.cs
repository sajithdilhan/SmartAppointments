using Scalar.AspNetCore;

namespace SmartAppointments.Gateway.Docs;

/// <summary>
/// Development-only aggregated API documentation: one Scalar page over the three services'
/// OpenAPI documents, which the gateway proxies at <c>/openapi/{service}/v1.json</c>
/// (the routes live in appsettings.Development.json).
/// </summary>
public static class GatewayDocsExtensions
{
    public static IEndpointConventionBuilder MapGatewayDocs(this IEndpointRouteBuilder endpoints)
    {
        var scalar = endpoints.MapScalarApiReference(options =>
        {
            options.WithTitle("Smart Appointments API")
                   .WithTheme(ScalarTheme.DeepSpace)
                   .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient)
                   .EnableDarkMode()
                   .AddDocument("auth", "Auth", "/openapi/auth/v1.json", isDefault: true)
                   .AddDocument("availability", "Availability", "/openapi/availability/v1.json")
                   .AddDocument("booking", "Booking", "/openapi/booking/v1.json");
            options.AddPreferredSecuritySchemes(["Bearer"]);
        });

        // The fallback policy would otherwise demand a token for the page itself.
        scalar.AllowAnonymous();
        return scalar;
    }
}
