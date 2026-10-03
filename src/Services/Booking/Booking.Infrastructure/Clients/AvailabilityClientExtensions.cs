using Booking.Application.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmartAppointments.BuildingBlocks;

namespace Booking.Infrastructure.Clients;

public static class AvailabilityClientExtensions
{
    public static IServiceCollection AddAvailabilityClient(this IServiceCollection services, IConfiguration configuration)
    {
        var baseUrl = configuration["Services:Availability:BaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl) || !Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
        {
            throw new InvalidOperationException(
                "'Services:Availability:BaseUrl' is not configured or is not an absolute URL. Set it to the " +
                "Availability service's address, for example 'http://localhost:5202'.");
        }

        var apiKey = configuration["InternalApi:Key"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "'InternalApi:Key' is not configured. It must equal the key Availability expects: set it with " +
                "'dotnet user-secrets' locally, or through the environment ('InternalApi__Key') everywhere else.");
        }

        services.AddHttpClient<IAvailabilityClient, AvailabilityClient>(client =>
            {
                client.BaseAddress = baseUri;
                // Never logged: LoggingMiddleware logs only the method and path.
                client.DefaultRequestHeaders.Add(Constants.ApiKeyHeaderName, apiKey);
            })
            .AddStandardResilienceHandler(options =>
            {
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(5);
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(20);
                // The standard retry strategy retries every HTTP method, POST included. That is safe
                // here only because Availability's reserve and release are idempotent per appointment
                // id. Do not add a non-idempotent call to this client without changing that.
                // The circuit breaker keeps its defaults. The 20 s total is far below the 2-minute
                // idempotency lease, so a live request never looks abandoned.
            });

        return services;
    }
}
