using Booking.Application.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmartAppointments.BuildingBlocks;
using SmartAppointments.BuildingBlocks.Web.Http;

namespace Booking.Infrastructure.Clients;

public static class AuthClientExtensions
{
    public static IServiceCollection AddAuthClient(this IServiceCollection services, IConfiguration configuration)
    {
        var baseUrl = configuration["Services:Auth:BaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl) || !Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
        {
            throw new InvalidOperationException(
                "'Services:Auth:BaseUrl' is not configured or is not an absolute URL. Set it to the " +
                "Auth service's address, for example 'http://localhost:5008'.");
        }

        var apiKey = configuration["InternalApi:Key"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "'InternalApi:Key' is not configured. It must equal the key Auth expects: set it with " +
                "'dotnet user-secrets' locally, or through the environment ('InternalApi__Key') everywhere else.");
        }

        services.AddHttpContextAccessor();
        services.AddTransient<CorrelationIdHandler>();

        services.AddHttpClient<IAuthClient, AuthClient>(client =>
            {
                client.BaseAddress = baseUri;
                // Never logged: LoggingMiddleware logs only the method and path.
                client.DefaultRequestHeaders.Add(Constants.ApiKeyHeaderName, apiKey);
            })
            // Before the resilience handler, so every retry carries the same correlation id.
            .AddHttpMessageHandler<CorrelationIdHandler>()
            .AddStandardResilienceHandler(options =>
            {
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(5);
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(20);
                // The standard retry strategy retries every HTTP method, POST included. That is safe
                // here because the lookup POST is a read: it changes nothing in Auth.
            });

        return services;
    }
}
