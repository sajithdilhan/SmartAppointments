using Availability.Application.Models;
using MediatR;
using SmartAppointments.BuildingBlocks.Models;

namespace Availability.Application.Commands;

public sealed record SetBranchScheduleCommand(
    Guid Id,
    string TimeZoneId,
    IReadOnlyList<WorkingHoursRequest> WorkingHours) : IRequest<Result<BranchResponse>>;
