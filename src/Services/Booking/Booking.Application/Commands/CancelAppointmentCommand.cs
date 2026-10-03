using MediatR;
using SmartAppointments.BuildingBlocks.Models;

namespace Booking.Application.Commands;

public sealed record CancelAppointmentCommand(Guid Id, Guid CallerId, string CallerRole) : IRequest<Result<bool>>;
