using Availability.Application.Models;
using MediatR;
using SmartAppointments.BuildingBlocks.Models;

namespace Availability.Application.Commands;

public sealed record UpdateServiceTypeCommand(
    Guid Id,
    string Name,
    string? Description,
    int DurationMinutes) : IRequest<Result<ServiceTypeResponse>>;
