using Availability.Application.Abstractions;
using Availability.Application.Commands;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using SmartAppointments.BuildingBlocks.Models;

namespace Availability.Application.Handlers;

public class ReleaseSlotCommandHandler(
    ISlotRepository slotRepository,
    ISlotReservationRepository reservationRepository,
    IValidator<ReleaseSlotCommand> validator,
    ILogger<ReleaseSlotCommandHandler> logger) : IRequestHandler<ReleaseSlotCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(ReleaseSlotCommand request, CancellationToken cancellationToken)
    {
        var validationResult = await validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            var validationErrors = string.Join(",", validationResult.Errors.Select(e => e.ErrorMessage).ToList());
            logger.LogWarning("Invalid request data for releasing a slot: {ValidationErrors}", validationErrors);
            return Result<bool>.Failure(new Error(400, $"Invalid request data. Errors: {validationErrors}"));
        }

        var slot = await slotRepository.GetByIdAsync(request.SlotId, cancellationToken);
        if (slot is null)
        {
            logger.LogWarning("Attempt to release a slot that does not exist: {SlotId}", request.SlotId);
            return Result<bool>.Failure(SlotErrors.NotFound(request.SlotId));
        }

        // No state checks: giving a place back must work whether or not the slot has started or
        // the branch is still open. Releasing a place that was never held is also a success.
        var outcome = await reservationRepository.ReleaseAsync(request.SlotId, request.AppointmentId, cancellationToken);

        logger.LogInformation(
            "Release of slot {SlotId} for appointment {AppointmentId}: {Outcome}",
            request.SlotId, request.AppointmentId, outcome);

        return Result<bool>.Success(true);
    }
}
