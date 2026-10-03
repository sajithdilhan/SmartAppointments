using Availability.Application.Models;
using MediatR;
using SmartAppointments.BuildingBlocks.Models;

namespace Availability.Application.Queries;

public sealed record SearchAvailableSlotsQuery(Guid BranchId, Guid ServiceTypeId, DateOnly? Date)
    : IRequest<Result<List<SlotResponse>>>;
