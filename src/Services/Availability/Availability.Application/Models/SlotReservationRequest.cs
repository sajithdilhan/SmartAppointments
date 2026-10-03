namespace Availability.Application.Models;

/// <summary>
/// The body of the internal reserve and release calls; the slot id is in the route.
/// </summary>
public sealed record SlotReservationRequest(Guid AppointmentId);
