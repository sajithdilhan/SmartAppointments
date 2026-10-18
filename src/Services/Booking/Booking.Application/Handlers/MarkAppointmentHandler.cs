using Booking.Application.Abstractions;
using Booking.Application.Commands;
using Booking.Application.Models;
using Booking.Domain.Entities;
using MediatR;
using SmartAppointments.BuildingBlocks;
using SmartAppointments.BuildingBlocks.Models;

namespace Booking.Application.Handlers;

public class MarkAppointmentHandler(IAppointmentRepository appointments, TimeProvider timeProvider)
    : IRequestHandler<MarkAppointmentCommand, Result<AppointmentResponse>>
{
    public async Task<Result<AppointmentResponse>> Handle(MarkAppointmentCommand request, CancellationToken cancellationToken)
    {
        var target = request.Outcome == AppointmentOutcome.Completed
            ? AppointmentStatus.Completed
            : AppointmentStatus.NoShow;

        var appointment = await appointments.GetByIdNoTrackingAsync(request.Id, cancellationToken);
        if (!CanAccess(appointment, request))
        {
            return Result<AppointmentResponse>.Failure(AppointmentErrors.NotFound(request.Id));
        }

        // Same outcome already recorded: a repeat is a 200 and changes nothing.
        var byStatus = AnswerForStatus(appointment!, target);
        if (byStatus is not null)
        {
            return byStatus;
        }

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        if (nowUtc < appointment!.StartUtc)
        {
            return Result<AppointmentResponse>.Failure(AppointmentErrors.NotStarted());
        }

        var changed = await appointments.TryTransitionAsync(appointment.Id, target, nowUtc, cancellationToken);

        // Re-read: after a win to return the stored row, after a lost race to answer for the winner.
        var current = await appointments.GetByIdNoTrackingAsync(appointment.Id, cancellationToken);
        if (!CanAccess(current, request))
        {
            return Result<AppointmentResponse>.Failure(AppointmentErrors.NotFound(request.Id));
        }

        if (!changed)
        {
            return AnswerForStatus(current!, target)
                   ?? Result<AppointmentResponse>.Failure(AppointmentErrors.NotMarkable(current!.Status, target));
        }

        return Result<AppointmentResponse>.Success(AppointmentResponse.From(current!));
    }

    // A staff member sees only their own branch; a missing claim (null) matches nothing.
    private static bool CanAccess(Appointment? appointment, MarkAppointmentCommand request) =>
        appointment is not null &&
        (request.CallerRole != Constants.StaffRole || request.CallerBranchId == appointment.BranchId);

    // Null means "still Booked, carry on".
    private static Result<AppointmentResponse>? AnswerForStatus(Appointment appointment, AppointmentStatus target)
    {
        if (appointment.Status == target)
        {
            return Result<AppointmentResponse>.Success(AppointmentResponse.From(appointment));
        }

        return appointment.Status == AppointmentStatus.Booked
            ? null
            : Result<AppointmentResponse>.Failure(AppointmentErrors.NotMarkable(appointment.Status, target));
    }
}
