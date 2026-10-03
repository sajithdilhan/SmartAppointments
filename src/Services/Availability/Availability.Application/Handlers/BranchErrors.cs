using SmartAppointments.BuildingBlocks.Models;

namespace Availability.Application.Handlers;

internal static class BranchErrors
{
    public static Error NotFound(Guid id) => new(404, $"Branch '{id}' was not found.");
}
