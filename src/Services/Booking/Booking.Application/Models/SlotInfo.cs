namespace Booking.Application.Models;

/// <summary>What Availability returns for a slot; mirrors its <c>InternalSlotResponse</c>.</summary>
public sealed record SlotInfo(
    Guid Id,
    Guid BranchId,
    Guid ServiceTypeId,
    DateTime StartUtc,
    DateTime EndUtc,
    int Capacity,
    int ReservedCount,
    int AvailableCapacity);
