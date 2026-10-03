using Booking.Domain.Entities;

namespace Booking.Application.Abstractions;

public enum AddAppointmentOutcome
{
    Saved,
    Overlap
}

public interface IAppointmentRepository
{
    /// <summary>Tracked, for handlers that change the appointment and call <see cref="SaveChangesAsync"/>.</summary>
    Task<Appointment?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<Appointment?> GetByIdNoTrackingAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> HasOverlappingBookedAsync(
        Guid customerId, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken);

    /// <summary>
    /// Owns its transaction: takes a per-customer lock, re-checks the overlap, then saves the
    /// appointment and the (already completed) idempotency record together. Returns
    /// <see cref="AddAppointmentOutcome.Overlap"/> and saves nothing if the customer is booked then.
    /// </summary>
    Task<AddAppointmentOutcome> TryAddBookedAsync(
        Appointment appointment, IdempotencyRecord completedRecord, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
