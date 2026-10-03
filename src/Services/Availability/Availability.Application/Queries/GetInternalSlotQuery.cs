using Availability.Application.Models;
using MediatR;
using SmartAppointments.BuildingBlocks.Models;

namespace Availability.Application.Queries;

public sealed record GetInternalSlotQuery(Guid Id) : IRequest<Result<InternalSlotResponse>>;
