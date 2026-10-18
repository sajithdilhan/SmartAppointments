using Auth.Application.Models;
using MediatR;
using SmartAppointments.BuildingBlocks.Models;

namespace Auth.Application.Commands;

public sealed record AssignStaffBranchCommand(Guid Id, Guid BranchId) : IRequest<Result<StaffResponse>>;
