using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Booking.Application.Abstractions;
using Booking.Application.Commands;
using Booking.Application.Models;
using Booking.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using SmartAppointments.BuildingBlocks.Models;

namespace Booking.Application.Handlers;

public class CreateAppointmentCommandHandler(
    IIdempotencyRepository idempotency,
    IAppointmentRepository appointments,
    IAvailabilityClient availability,
    IValidator<CreateAppointmentCommand> validator,
    TimeProvider timeProvider,
    ILogger<CreateAppointmentCommandHandler> logger) : IRequestHandler<CreateAppointmentCommand, Result<AppointmentResponse>>
{
    public async Task<Result<AppointmentResponse>> Handle(CreateAppointmentCommand request, CancellationToken cancellationToken)
    {
        var validationResult = await validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            var validationErrors = string.Join(",", validationResult.Errors.Select(e => e.ErrorMessage).ToList());
            logger.LogWarning("Invalid request data for creating an appointment: {ValidationErrors}", validationErrors);
            return Result<AppointmentResponse>.Failure(new Error(400, $"Invalid request data. Errors: {validationErrors}"));
        }

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var claim = await idempotency.ClaimAsync(
            request.CustomerId, request.IdempotencyKey!, HashRequest(request), Guid.CreateVersion7(), nowUtc, cancellationToken);

        switch (claim)
        {
            case ClaimResult.Replay replay:
                return Replay(replay);
            case ClaimResult.HashMismatch:
                return Result<AppointmentResponse>.Failure(AppointmentErrors.KeyMismatch());
            case ClaimResult.InProgress:
                return Result<AppointmentResponse>.Failure(AppointmentErrors.KeyInProgress());
            case ClaimResult.Claimed claimed:
                return await Process(request, claimed.Record, nowUtc, cancellationToken);
            default:
                throw new InvalidOperationException($"Unexpected claim result {claim.GetType().Name}.");
        }
    }

    private async Task<Result<AppointmentResponse>> Process(
        CreateAppointmentCommand request, IdempotencyRecord record, DateTime nowUtc, CancellationToken cancellationToken)
    {
        // Set just before the reserve call is issued, cleared only by a definite refusal from
        // Availability. While true, the outcome of the reserve may be unknown to us.
        var reserveAttempted = false;
        // The record carries the appointment id: an earlier, crashed attempt's id when the key was taken over.
        var appointmentId = record.AppointmentId;

        try
        {
            var slotResult = await availability.GetSlotAsync(request.SlotId, cancellationToken);
            if (!slotResult.IsSuccess)
            {
                return slotResult.Error!.Status == 404
                    ? await Store(record, AppointmentErrors.SlotNotFound(), nowUtc, cancellationToken)
                    : await AbandonBeforeReserve(record, request.SlotId);
            }

            var slot = slotResult.Value!;

            if (await appointments.HasOverlappingBookedAsync(request.CustomerId, slot.StartUtc, slot.EndUtc, cancellationToken))
            {
                return await Store(record, AppointmentErrors.Overlap(), nowUtc, cancellationToken);
            }

            reserveAttempted = true;
            var reserve = await availability.ReserveAsync(request.SlotId, appointmentId, cancellationToken);
            if (!reserve.IsSuccess)
            {
                var status = reserve.Error!.Status;
                if (status is 404 or 409)
                {
                    // A definite refusal: nothing is held, so this is not a reserve attempt.
                    reserveAttempted = false;
                    var error = status == 404 ? AppointmentErrors.SlotNotFound() : reserve.Error;
                    return await Store(record, error, nowUtc, cancellationToken);
                }

                return await LeaveInProgress(request.SlotId, appointmentId);
            }

            var appointment = Appointment.Book(
                appointmentId, request.CustomerId, request.SlotId, slot.BranchId, slot.ServiceTypeId,
                slot.StartUtc, slot.EndUtc, nowUtc);
            var response = AppointmentResponse.From(appointment);
            record.Complete(201, JsonSerializer.Serialize(response, StoredResponseJson.Options), nowUtc);

            var outcome = await appointments.TryAddBookedAsync(appointment, record, cancellationToken);
            if (outcome == AddAppointmentOutcome.Overlap)
            {
                // Lost the race against another request by the same customer: give the place back.
                var release = await availability.ReleaseAsync(request.SlotId, appointmentId, CancellationToken.None);
                if (!release.IsSuccess)
                {
                    return await LeaveInProgress(request.SlotId, appointmentId, release: false);
                }

                reserveAttempted = false;
                return await Store(record, AppointmentErrors.Overlap(), nowUtc, cancellationToken);
            }

            return Result<AppointmentResponse>.Success(response);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Creating an appointment for slot {SlotId} failed unexpectedly", request.SlotId);
            if (reserveAttempted)
            {
                // The claim stays in progress; the reservation is released best effort.
                await TryRelease(request.SlotId, appointmentId);
            }
            else
            {
                await TryRemove(record);
            }

            throw;
        }
    }

    private async Task<Result<AppointmentResponse>> Store(
        IdempotencyRecord record, Error error, DateTime nowUtc, CancellationToken cancellationToken)
    {
        await idempotency.CompleteAsync(
            record, error.Status, JsonSerializer.Serialize(error, StoredResponseJson.Options), nowUtc, cancellationToken);
        return Result<AppointmentResponse>.Failure(error);
    }

    // The slot could not be read: nothing was reserved, so free the key for an immediate retry.
    private async Task<Result<AppointmentResponse>> AbandonBeforeReserve(IdempotencyRecord record, Guid slotId)
    {
        logger.LogWarning("Availability is unavailable while reading slot {SlotId}; the idempotency claim is removed", slotId);
        await idempotency.RemoveAsync(record, CancellationToken.None);
        return Result<AppointmentResponse>.Failure(AppointmentErrors.Unavailable());
    }

    // The reserve may or may not have happened. Release once, best effort, and leave the claim in
    // progress with its appointment id: a retry after the lease reuses that id, so the reserve is
    // found (release failed) or made afresh (release succeeded), never counted twice.
    private async Task<Result<AppointmentResponse>> LeaveInProgress(Guid slotId, Guid appointmentId, bool release = true)
    {
        logger.LogWarning(
            "Outcome of reserving slot {SlotId} for appointment {AppointmentId} is unknown; the claim is left in progress",
            slotId, appointmentId);
        if (release)
        {
            await TryRelease(slotId, appointmentId);
        }

        return Result<AppointmentResponse>.Failure(AppointmentErrors.Unavailable());
    }

    private async Task TryRelease(Guid slotId, Guid appointmentId)
    {
        try
        {
            var release = await availability.ReleaseAsync(slotId, appointmentId, CancellationToken.None);
            if (!release.IsSuccess)
            {
                logger.LogWarning(
                    "Compensating release of slot {SlotId} for appointment {AppointmentId} failed: {Details}",
                    slotId, appointmentId, release.Error!.Details);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Compensating release of slot {SlotId} for appointment {AppointmentId} threw", slotId, appointmentId);
        }
    }

    private async Task TryRemove(IdempotencyRecord record)
    {
        try
        {
            await idempotency.RemoveAsync(record, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Removing the idempotency claim {RecordId} failed", record.Id);
        }
    }

    private static Result<AppointmentResponse> Replay(ClaimResult.Replay replay)
    {
        if (replay.StatusCode == 201)
        {
            var appointment = JsonSerializer.Deserialize<AppointmentResponse>(replay.ResponseBody, StoredResponseJson.Options)
                ?? throw new InvalidOperationException("The stored idempotent response is empty.");
            return Result<AppointmentResponse>.Success(appointment);
        }

        var error = JsonSerializer.Deserialize<Error>(replay.ResponseBody, StoredResponseJson.Options)
            ?? throw new InvalidOperationException("The stored idempotent error is empty.");
        return Result<AppointmentResponse>.Failure(error);
    }

    // Only what the client controls: the slot. The key and the user are the record's identity.
    internal static string HashRequest(CreateAppointmentCommand request)
    {
        var canonical = request.SlotId.ToString("D").ToLowerInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
}
