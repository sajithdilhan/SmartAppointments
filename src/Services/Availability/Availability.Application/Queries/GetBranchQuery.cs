using Availability.Application.Models;
using MediatR;
using SmartAppointments.BuildingBlocks.Models;

namespace Availability.Application.Queries;

public sealed record GetBranchQuery(Guid Id, string? CurrentUserRole) : IRequest<Result<BranchResponse>>;
