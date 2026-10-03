using Availability.Application.Abstractions;
using Availability.Application.Commands;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using SmartAppointments.BuildingBlocks.Models;

namespace Availability.Application.Handlers;

public class ReserveSlotCommandHandler(
    ISlotRepository slotRepository,
    ISlotReservationRepository reservationRepository,
    IBranchRepository branchRepository,
    IServiceTypeRepository serviceTypeRepository,
    IValidator<ReserveSlotCommand> validator,
    TimeProvider timeProvider,
    ILogger<ReserveSlotCommandHandler> logger) : IRequestHandler<ReserveSlotCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(ReserveSlotCommand request, CancellationToken cancellationToken)
    {
        var validationResult = await validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            var validationErrors = string.Join(",", validationResult.Errors.Select(e => e.ErrorMessage).ToList());
            logger.LogWarning("Invalid request data for reserving a slot: {ValidationErrors}", validationErrors);
            return Result<bool>.Failure(new Error(400, $"Invalid request data. Errors: {validationErrors}"));
        }

        var slot = await slotRepository.GetByIdAsync(request.SlotId, cancellationToken);
        if (slot is null)
        {
            logger.LogWarning("Attempt to reserve a slot that does not exist: {SlotId}", request.SlotId);
            return Result<bool>.Failure(SlotErrors.NotFound(request.SlotId));
        }

        // A repeat of a reserve that already succeeded must succeed again, even if the slot has
        // started or the branch has closed since, so this runs before the state checks.
        if (await reservationRepository.ExistsAsync(request.SlotId, request.AppointmentId, cancellationToken))
        {
            return Result<bool>.Success(true);
        }

        var branch = await branchRepository.GetByIdAsync(slot.BranchId, cancellationToken);
        var serviceType = await serviceTypeRepository.GetByIdAsync(slot.ServiceTypeId, cancellationToken);
        if (branch is null || serviceType is null)
        {
            // The Restrict foreign keys make this unreachable; treat it as the slot being gone.
            logger.LogError("Slot {SlotId} refers to a missing branch or service type", slot.Id);
            return Result<bool>.Failure(SlotErrors.NotFound(request.SlotId));
        }

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        if (slot.StartUtc <= nowUtc)
        {
            return Result<bool>.Failure(SlotErrors.AlreadyStarted());
        }

        if (!branch.IsActive)
        {
            return Result<bool>.Failure(SlotErrors.BranchInactive(branch.Code));
        }

        if (!serviceType.IsActive)
        {
            return Result<bool>.Failure(SlotErrors.ServiceTypeInactive(serviceType.Code));
        }

        // The authority on duplicates and capacity: a concurrent duplicate can slip in between
        // the early exit above and here.
        var outcome = await reservationRepository.ReserveAsync(
            request.SlotId, request.AppointmentId, nowUtc, cancellationToken);

        if (outcome == ReserveOutcome.Full)
        {
            return Result<bool>.Failure(SlotErrors.Full());
        }

        logger.LogInformation(
            "Reserve of slot {SlotId} for appointment {AppointmentId}: {Outcome}",
            request.SlotId, request.AppointmentId, outcome);

        return Result<bool>.Success(true);
    }
}
