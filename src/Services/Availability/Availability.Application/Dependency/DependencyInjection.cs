using Availability.Application.Commands;
using Availability.Application.Queries;
using Availability.Application.Validations;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Availability.Application.Dependency;

// Token validation, the authorization policies and OpenAPI registration are shared by every
// service and live in SmartAppointments.BuildingBlocks.Web; Program.cs calls them directly.
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Handlers take the clock from here so tests can pin "now".
        services.AddSingleton(TimeProvider.System);
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(DependencyInjection).Assembly));
        services.AddScoped<IValidator<CreateBranchCommand>, CreateBranchCommandValidator>();
        services.AddScoped<IValidator<UpdateBranchCommand>, UpdateBranchCommandValidator>();
        services.AddScoped<IValidator<CreateServiceTypeCommand>, CreateServiceTypeCommandValidator>();
        services.AddScoped<IValidator<UpdateServiceTypeCommand>, UpdateServiceTypeCommandValidator>();
        services.AddScoped<IValidator<SetBranchScheduleCommand>, SetBranchScheduleCommandValidator>();
        services.AddScoped<IValidator<GenerateSlotsCommand>, GenerateSlotsCommandValidator>();
        services.AddScoped<IValidator<SearchAvailableSlotsQuery>, SearchAvailableSlotsQueryValidator>();
        services.AddScoped<IValidator<ReserveSlotCommand>, ReserveSlotCommandValidator>();
        services.AddScoped<IValidator<ReleaseSlotCommand>, ReleaseSlotCommandValidator>();
        return services;
    }
}
