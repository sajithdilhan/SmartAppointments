using SmartAppointments.BuildingBlocks.Models;

namespace Booking.Application.Handlers;

internal static class AppointmentErrors
{
    // Deliberately the same for "does not exist" and "not yours", so ids cannot be probed.
    public static Error NotFound(Guid id) => new(404, $"Appointment '{id}' was not found.");

    public static Error SlotNotFound() => new(404, "Slot not found.");

    public static Error Overlap() => new(409, "You already have an appointment at that time.");

    public static Error KeyMismatch() => new(422, "This Idempotency-Key was already used with a different request.");

    public static Error KeyInProgress() => new(409, "A request with this Idempotency-Key is still being processed.");

    public static Error AlreadyStarted() => new(409, "The appointment has already started and can no longer be cancelled.");

    public static Error Unavailable() => new(503, "The availability service is unavailable. Try again later.");
}
