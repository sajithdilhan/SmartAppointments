using Booking.Application.Abstractions;
using Booking.Infrastructure.Clients;
using Booking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Booking.Infrastructure.Dependency;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Left blank in appsettings.json on purpose: it carries the database password, so it is
        // supplied from user-secrets locally and from the environment everywhere else.
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Connection string 'DefaultConnection' is not configured. Set it with " +
                "'dotnet user-secrets set \"ConnectionStrings:DefaultConnection\" \"<connection string>\"' " +
                "for local development, or through the environment in every other environment.");
        }

        services.AddAvailabilityClient(configuration);

        services.AddScoped<IAppointmentRepository, AppointmentRepository>();
        services.AddScoped<IIdempotencyRepository, IdempotencyRepository>();
        services.AddDbContext<ApplicationDbContext>(options =>
        {
            options.UseNpgsql(connectionString);
        });

        services.AddHealthChecks().AddNpgSql(connectionString);

        return services;
    }
}
