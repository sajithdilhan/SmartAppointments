using Availability.Application.Models;
using MediatR;
using SmartAppointments.BuildingBlocks.Models;

namespace Availability.Application.Commands;

public sealed record UpdateBranchCommand(
    Guid Id,
    string Name,
    string? Description,
    string Address,
    string PhoneNumber) : IRequest<Result<BranchResponse>>;
