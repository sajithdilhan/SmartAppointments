namespace Availability.Domain.Entities;

/// <summary>
/// One bookable time window for a branch and service type. Instants are stored in UTC, because
/// that is what Booking compares and what events carry. The local date is stored too, because
/// "the slots on 5 October in Colombo" is a local-date question; it is fixed at generation time,
/// which is correct because a slot never moves.
/// </summary>
public sealed class Slot
{
    private Slot() { }

    public Guid Id { get; private set; }

    public Guid BranchId { get; private set; }

    public Guid ServiceTypeId { get; private set; }

    /// <summary>
    /// The date at the branch, in its time zone, on which the slot starts.
    /// </summary>
    public DateOnly LocalDate { get; private set; }

    public DateTime StartUtc { get; private set; }

    public DateTime EndUtc { get; private set; }

    public int Capacity { get; private set; }

    /// <summary>
    /// Always 0 until booking reserves capacity.
    /// </summary>
    public int ReservedCount { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public int AvailableCapacity => Capacity - ReservedCount;

    public static Slot Create(
        Guid branchId,
        Guid serviceTypeId,
        DateOnly localDate,
        DateTime startUtc,
        DateTime endUtc,
        int capacity)
    {
        return new Slot
        {
            Id = Guid.CreateVersion7(),
            BranchId = branchId,
            ServiceTypeId = serviceTypeId,
            LocalDate = localDate,
            StartUtc = startUtc,
            EndUtc = endUtc,
            Capacity = capacity,
            ReservedCount = 0,
            CreatedAtUtc = DateTime.UtcNow
        };
    }
}
