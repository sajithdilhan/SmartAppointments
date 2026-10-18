using Booking.Application.Models;
using MediatR;
using SmartAppointments.BuildingBlocks.Models;

namespace Booking.Application.Queries;

/// <param name="CallerBranchId">The staff member's <c>branch_id</c> claim, or null when absent or malformed.</param>
public sealed record GetAppointmentQuery(Guid Id, Guid CallerId, string CallerRole, Guid? CallerBranchId) : IRequest<Result<AppointmentResponse>>;
