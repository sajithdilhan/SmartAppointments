using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Booking.Application.Dependency;

// Token validation, the authorization policies and OpenAPI registration are shared by every
// service and live in SmartAppointments.BuildingBlocks.Web; Program.cs calls them directly.
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Handlers take the clock from here so tests can pin "now".
        services.AddSingleton(TimeProvider.System);
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(DependencyInjection).Assembly));
        // One validator per command is registered here as the commands arrive.
        return services;
    }
}
