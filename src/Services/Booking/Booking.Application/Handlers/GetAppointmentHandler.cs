using Booking.Application.Abstractions;
using Booking.Application.Models;
using Booking.Application.Queries;
using MediatR;
using SmartAppointments.BuildingBlocks;
using SmartAppointments.BuildingBlocks.Models;

namespace Booking.Application.Handlers;

public class GetAppointmentHandler(IAppointmentRepository appointments)
    : IRequestHandler<GetAppointmentQuery, Result<AppointmentResponse>>
{
    public async Task<Result<AppointmentResponse>> Handle(GetAppointmentQuery request, CancellationToken cancellationToken)
    {
        var appointment = await appointments.GetByIdNoTrackingAsync(request.Id, cancellationToken);

        var allowed = appointment is not null &&
            (appointment.CustomerId == request.CallerId ||
             request.CallerRole is Constants.StaffRole or Constants.AdminRole);

        return allowed
            ? Result<AppointmentResponse>.Success(AppointmentResponse.From(appointment!))
            : Result<AppointmentResponse>.Failure(AppointmentErrors.NotFound(request.Id));
    }
}
