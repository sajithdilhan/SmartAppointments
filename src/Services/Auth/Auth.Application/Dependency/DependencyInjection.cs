using Auth.Application.Commands;
using Auth.Application.Validations;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Auth.Application.Dependency;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Register application services here
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(DependencyInjection).Assembly));
        services.AddScoped<IValidator<RegisterCustomerCommand>, RegisterCustomerCommandValidator>();
        services.AddScoped<IValidator<LoginUserCommand>, LoginUserRequestValidator>();
        return services;
    }
}
