using Booking.Application.Abstractions;
using Booking.Domain.Entities;

namespace Booking.Application.Models;

/// <summary>Every <see cref="AppointmentResponse"/> field, flattened, plus the customer's name and phone (null when unknown or unavailable).</summary>
public sealed record BranchAppointmentResponse(
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
    DateTime? CancelledAtUtc,
    DateTime? CompletedAtUtc,
    DateTime? NoShowAtUtc,
    CustomerSummary? Customer)
{
    public static BranchAppointmentResponse From(Appointment a, CustomerInfo? customer) => new(
        a.Id, a.CustomerId, a.SlotId, a.BranchId, a.ServiceTypeId, a.StartUtc, a.EndUtc,
        a.Status, a.CreatedAtUtc, a.UpdatedAtUtc, a.CancelledAtUtc, a.CompletedAtUtc, a.NoShowAtUtc,
        customer is null ? null : new CustomerSummary(customer.FirstName, customer.LastName, customer.PhoneNumber));
}
