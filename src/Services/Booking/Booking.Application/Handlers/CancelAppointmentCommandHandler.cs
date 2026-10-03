using Booking.Application.Abstractions;
using Booking.Application.Commands;
using Booking.Domain.Entities;
using MediatR;
using Microsoft.Extensions.Logging;
using SmartAppointments.BuildingBlocks;
using SmartAppointments.BuildingBlocks.Models;

namespace Booking.Application.Handlers;

public class CancelAppointmentCommandHandler(
    IAppointmentRepository appointments,
    IAvailabilityClient availability,
    TimeProvider timeProvider,
    ILogger<CancelAppointmentCommandHandler> logger) : IRequestHandler<CancelAppointmentCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(CancelAppointmentCommand request, CancellationToken cancellationToken)
    {
        var appointment = await appointments.GetByIdAsync(request.Id, cancellationToken);

        // Staff can view an appointment but not cancel someone else's: they get the same 404.
        var allowed = appointment is not null &&
            (appointment.CustomerId == request.CallerId || request.CallerRole == Constants.AdminRole);
        if (!allowed)
        {
            return Result<bool>.Failure(AppointmentErrors.NotFound(request.Id));
        }

        // A repeat cancel skips the checks and goes straight to the release, which is what lets a
        // customer finish a cancel whose release failed, even after the start time has passed.
        if (appointment!.Status != AppointmentStatus.Cancelled)
        {
            var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
            if (appointment.StartUtc <= nowUtc)
            {
                return Result<bool>.Failure(AppointmentErrors.AlreadyStarted());
            }

            appointment.Cancel(nowUtc);
            await appointments.SaveChangesAsync(cancellationToken);
        }

        var release = await availability.ReleaseAsync(appointment.SlotId, appointment.Id, cancellationToken);
        if (!release.IsSuccess)
        {
            // The appointment stays Cancelled; a repeat cancel re-releases.
            logger.LogWarning(
                "Appointment {AppointmentId} is cancelled but releasing slot {SlotId} failed: {Details}",
                appointment.Id, appointment.SlotId, release.Error!.Details);
            return Result<bool>.Failure(AppointmentErrors.Unavailable());
        }

        return Result<bool>.Success(true);
    }
}
