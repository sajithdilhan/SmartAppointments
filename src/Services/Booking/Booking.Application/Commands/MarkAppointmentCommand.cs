using Booking.Application.Models;
using MediatR;
using SmartAppointments.BuildingBlocks.Models;

namespace Booking.Application.Commands;

public enum AppointmentOutcome
{
    Completed,
    NoShow
}

/// <param name="CallerBranchId">The staff member's <c>branch_id</c> claim, or null when absent or malformed.</param>
public sealed record MarkAppointmentCommand(
    Guid Id, AppointmentOutcome Outcome, Guid CallerId, string CallerRole, Guid? CallerBranchId)
    : IRequest<Result<AppointmentResponse>>;
