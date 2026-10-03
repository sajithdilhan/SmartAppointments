using Availability.Domain.Entities;

namespace Availability.Application.Models;

public sealed record ServiceTypeResponse(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    int DurationMinutes,
    bool IsActive,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc)
{
    public static ServiceTypeResponse From(ServiceType serviceType) => new(
        serviceType.Id,
        serviceType.Code,
        serviceType.Name,
        serviceType.Description,
        serviceType.DurationMinutes,
        serviceType.IsActive,
        serviceType.CreatedAtUtc,
        serviceType.UpdatedAtUtc);
}
