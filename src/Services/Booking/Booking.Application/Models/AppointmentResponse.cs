using Booking.Domain.Entities;

namespace Booking.Application.Models;

public sealed record AppointmentResponse(
    Guid Id,
    Guid CustomerId,
    Guid SlotId,
    Guid BranchId,
    Guid ServiceTypeId,
    DateTime StartUtc,
    DateTime EndUtc,
    AppointmentStatus Status,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    DateTime? CancelledAtUtc)
{
    public static AppointmentResponse From(Appointment a) => new(
        a.Id, a.CustomerId, a.SlotId, a.BranchId, a.ServiceTypeId, a.StartUtc, a.EndUtc,
        a.Status, a.CreatedAtUtc, a.UpdatedAtUtc, a.CancelledAtUtc);
}
