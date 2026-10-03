using Booking.Application.Models;
using MediatR;
using SmartAppointments.BuildingBlocks.Models;

namespace Booking.Application.Commands;

// IdempotencyKey is nullable because a missing header must reach the validator (a 400 with our message).
public sealed record CreateAppointmentCommand(Guid CustomerId, string? IdempotencyKey, Guid SlotId)
    : IRequest<Result<AppointmentResponse>>;
