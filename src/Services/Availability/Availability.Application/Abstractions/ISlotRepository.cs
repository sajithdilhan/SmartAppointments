using Availability.Domain.Entities;

namespace Availability.Application.Abstractions;

public interface ISlotRepository
{
    /// <summary>
    /// Stages the slots only; nothing is written until <see cref="SaveChangesAsync"/>.
    /// </summary>
    Task AddRangeAsync(IEnumerable<Slot> slots, CancellationToken cancellationToken);

    /// <summary>
    /// The existing slots for the pair that overlap the window, read with no tracking.
    /// </summary>
    Task<List<Slot>> ListOverlappingAsync(
        Guid branchId, Guid serviceTypeId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken);

    /// <summary>
    /// The slots on a local date that start after <paramref name="afterUtc"/> and still have
    /// capacity, ordered by start time, read with no tracking.
    /// </summary>
    Task<List<Slot>> SearchAvailableAsync(
        Guid branchId, Guid serviceTypeId, DateOnly localDate, DateTime afterUtc, CancellationToken cancellationToken);

    /// <exception cref="DuplicateSlotException">A concurrent request created the same slot.</exception>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
