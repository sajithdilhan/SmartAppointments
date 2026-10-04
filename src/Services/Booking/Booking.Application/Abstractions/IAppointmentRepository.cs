using Booking.Application.Models;
using Booking.Domain.Entities;

namespace Booking.Application.Abstractions;

public enum AddAppointmentOutcome
{
    Saved,
    Overlap
}

public sealed record AppointmentPage(IReadOnlyList<Appointment> Items, int TotalCount);

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

    /// <summary>
    /// One customer's appointments, filtered, in the fixed order (soonest first when <paramref name="when"/>
    /// is Upcoming, otherwise most recent first, ties by Id ascending), one page, plus the total that
    /// match across all pages. No tracking.
    /// </summary>
    Task<AppointmentPage> ListForCustomerAsync(
        Guid customerId, AppointmentStatus? status, AppointmentTimeFilter? when, DateTime nowUtc,
        int page, int pageSize, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
