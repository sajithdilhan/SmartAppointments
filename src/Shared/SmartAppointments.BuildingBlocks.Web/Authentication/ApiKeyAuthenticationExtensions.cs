using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Text;

namespace SmartAppointments.BuildingBlocks.Web.Authentication;

public static class ApiKeyAuthenticationExtensions
{
    /// <summary>
    /// Adds the API-key scheme and <see cref="Constants.InternalServicePolicy"/> for the internal
    /// endpoints other services call. It does not touch the default schemes <c>AddJwtAuthentication</c>
    /// sets, so every existing <c>[Authorize]</c> keeps meaning what it meant, and the policy names
    /// only this scheme, so a bearer token never reaches an internal endpoint.
    /// </summary>
    public static IServiceCollection AddApiKeyAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var key = configuration["InternalApi:Key"];

        // Like the JWT secret, the key is deliberately absent from appsettings.json, so an
        // unconfigured environment fails loudly at startup instead of accepting a blank key.
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException(
                "'InternalApi:Key' is not configured. Set it in appsettings.Development.json or with " +
                "'dotnet user-secrets set \"InternalApi:Key\" \"<key>\"' for local development, or through " +
                "the environment in every other environment. It must be the same value in every service.");
        }

        if (Encoding.UTF8.GetByteCount(key) < AuthenticationExtensions.MinimumSecretKeyBytes)
        {
            throw new InvalidOperationException(
                $"'InternalApi:Key' must be at least {AuthenticationExtensions.MinimumSecretKeyBytes} bytes long.");
        }

        services.AddAuthentication()
            .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
                Constants.ApiKeyAuthenticationScheme,
                options => options.ApiKey = key);

        services.AddAuthorizationBuilder()
            .AddPolicy(Constants.InternalServicePolicy, policy => policy
                .AddAuthenticationSchemes(Constants.ApiKeyAuthenticationScheme)
                .RequireAuthenticatedUser());

        return services;
    }
}
