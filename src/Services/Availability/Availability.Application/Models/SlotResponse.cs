using Availability.Domain.Entities;

namespace Availability.Application.Models;

public sealed record SlotResponse(
    Guid Id,
    Guid BranchId,
    Guid ServiceTypeId,
    DateOnly Date,
    DateTime StartAtUtc,
    DateTime EndAtUtc,
    TimeOnly LocalStartTime,
    TimeOnly LocalEndTime,
    int Capacity,
    int AvailableCapacity)
{
    public static SlotResponse From(Slot slot, TimeZoneInfo branchZone) => new(
        slot.Id,
        slot.BranchId,
        slot.ServiceTypeId,
        slot.LocalDate,
        slot.StartUtc,
        slot.EndUtc,
        TimeOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(slot.StartUtc, branchZone)),
        TimeOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(slot.EndUtc, branchZone)),
        slot.Capacity,
        slot.AvailableCapacity);
}
