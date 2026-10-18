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

    /// <summary>
    /// One branch's appointments starting in [<paramref name="fromUtc"/>, <paramref name="toUtc"/>), optionally by
    /// status, soonest first (ties by Id), one page, plus the total across all pages. No tracking.
    /// </summary>
    Task<AppointmentPage> ListForBranchAsync(
        Guid branchId, DateTime fromUtc, DateTime toUtc, AppointmentStatus? status,
        int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>
    /// One conditional update: moves a still-Booked appointment to <paramref name="to"/> (Cancelled,
    /// Completed or NoShow), setting UpdatedAtUtc and the matching timestamp. Returns false when no
    /// row changed (unknown id, or the status was no longer Booked), so the caller re-reads.
    /// </summary>
    Task<bool> TryTransitionAsync(Guid id, AppointmentStatus to, DateTime nowUtc, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
