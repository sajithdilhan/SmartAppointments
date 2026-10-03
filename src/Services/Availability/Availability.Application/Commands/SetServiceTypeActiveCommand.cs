using MediatR;
using SmartAppointments.BuildingBlocks.Models;

namespace Availability.Application.Commands;

public sealed record SetServiceTypeActiveCommand(Guid Id, bool IsActive) : IRequest<Result<bool>>;
