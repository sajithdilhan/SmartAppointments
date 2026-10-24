using MediatR;
using SmartAppointments.BuildingBlocks.Models;

namespace Auth.Application.Commands;

public sealed record SetStaffActiveCommand(Guid Id, bool IsActive) : IRequest<Result<Unit>>;
