namespace Availability.Application.Models;

public sealed record CreateServiceTypeRequest(
    string Code,
    string Name,
    string? Description,
    int DurationMinutes);
