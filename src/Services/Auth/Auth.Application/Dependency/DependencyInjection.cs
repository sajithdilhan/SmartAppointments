using Auth.Application.Commands;
using Auth.Application.Validations;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Auth.Application.Dependency;

// Token validation, the authorization policies and OpenAPI registration are shared by every
// service and live in SmartAppointments.BuildingBlocks.Web; Program.cs calls them directly.
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(DependencyInjection).Assembly));
        services.AddScoped<IValidator<RegisterCustomerCommand>, RegisterCustomerCommandValidator>();
        services.AddScoped<IValidator<LoginUserCommand>, LoginUserRequestValidator>();
        return services;
    }
}
