using Availability.Application.Models;
using MediatR;
using SmartAppointments.BuildingBlocks.Models;

namespace Availability.Application.Commands;

public sealed record GenerateSlotsCommand(
    Guid BranchId,
    Guid ServiceTypeId,
    DateOnly FromDate,
    DateOnly ToDate,
    int Capacity) : IRequest<Result<GenerateSlotsResponse>>;
