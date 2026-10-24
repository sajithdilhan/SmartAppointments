using Auth.Application.Models;
using MediatR;
using SmartAppointments.BuildingBlocks.Models;

namespace Auth.Application.Queries;

public sealed record GetStaffQuery(Guid Id) : IRequest<Result<StaffResponse>>;
