using Availability.Application.Models;
using MediatR;
using SmartAppointments.BuildingBlocks.Models;

namespace Availability.Application.Queries;

public sealed record GetBranchesQuery() : IRequest<Result<List<BranchDto>>>;

