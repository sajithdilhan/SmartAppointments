using MediatR;
using SmartAppointments.BuildingBlocks.Models;

namespace Auth.Application.Commands;

public sealed record DeleteStaffCommand(Guid Id) : IRequest<Result<Unit>>;
