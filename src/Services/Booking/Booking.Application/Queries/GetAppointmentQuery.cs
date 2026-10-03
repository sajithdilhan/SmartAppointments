using Booking.Application.Models;
using MediatR;
using SmartAppointments.BuildingBlocks.Models;

namespace Booking.Application.Queries;

public sealed record GetAppointmentQuery(Guid Id, Guid CallerId, string CallerRole) : IRequest<Result<AppointmentResponse>>;
