using SmartAppointments.BuildingBlocks.Models;

namespace Availability.Application.Handlers;

internal static class ServiceTypeErrors
{
    public static Error NotFound(Guid id) => new(404, $"Service type '{id}' was not found.");

    public static Error DuplicateCode(string code) => new(409, $"A service type with code '{code}' already exists.");
}
