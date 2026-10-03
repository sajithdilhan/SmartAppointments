namespace Availability.Application.Abstractions;

public enum ReserveOutcome { Reserved, AlreadyReserved, Full }

public enum ReleaseOutcome { Released, NotReserved }

/// <summary>
/// Unlike the stage-then-<c>SaveChangesAsync</c> repositories, the two write methods own their
/// transactions: the correctness argument is one transaction containing one conditional
/// statement, and splitting it across this abstraction would let a caller get it wrong.
/// </summary>
public interface ISlotReservationRepository
{
    /// <summary>
    /// Read-only: has this appointment already been given a place in the slot?
    /// </summary>
    Task<bool> ExistsAsync(Guid slotId, Guid appointmentId, CancellationToken cancellationToken);

    /// <summary>
    /// Takes one place for the appointment. A repeat for the same pair is
    /// <see cref="ReserveOutcome.AlreadyReserved"/> and changes nothing; a slot with no place left
    /// is <see cref="ReserveOutcome.Full"/> and also changes nothing.
    /// </summary>
    Task<ReserveOutcome> ReserveAsync(Guid slotId, Guid appointmentId, DateTime nowUtc, CancellationToken cancellationToken);

    /// <summary>
    /// Gives the appointment's place back. Releasing a pair that holds no place is
    /// <see cref="ReleaseOutcome.NotReserved"/> and changes nothing.
    /// </summary>
    Task<ReleaseOutcome> ReleaseAsync(Guid slotId, Guid appointmentId, CancellationToken cancellationToken);
}
