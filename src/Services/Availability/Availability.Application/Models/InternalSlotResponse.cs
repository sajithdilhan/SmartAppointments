using Availability.Domain.Entities;

namespace Availability.Application.Models;

/// <summary>
/// What Booking needs to know about a slot: the window it copies into the appointment, and the
/// counts. Unlike <see cref="SlotResponse"/> it has no branch-local times.
/// </summary>
public sealed record InternalSlotResponse(
    Guid Id,
    Guid BranchId,
    Guid ServiceTypeId,
    DateTime StartUtc,
    DateTime EndUtc,
    int Capacity,
    int ReservedCount,
    int AvailableCapacity)
{
    public static InternalSlotResponse From(Slot slot) => new(
        slot.Id,
        slot.BranchId,
        slot.ServiceTypeId,
        slot.StartUtc,
        slot.EndUtc,
        slot.Capacity,
        slot.ReservedCount,
        slot.AvailableCapacity);
}
