using Auth.Application.Models;
using MediatR;
using SmartAppointments.BuildingBlocks.Models;

namespace Auth.Application.Queries;

public sealed record LookupUsersQuery(IReadOnlyList<Guid>? Ids) : IRequest<Result<IReadOnlyList<UserLookupItem>>>;
