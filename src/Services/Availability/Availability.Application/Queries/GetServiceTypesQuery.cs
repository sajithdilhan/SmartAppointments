using Availability.Application.Models;
using MediatR;
using SmartAppointments.BuildingBlocks.Models;

namespace Availability.Application.Queries;

public sealed record GetServiceTypesQuery(bool IncludeInactive, string? CurrentUserRole) : IRequest<Result<List<ServiceTypeResponse>>>;
