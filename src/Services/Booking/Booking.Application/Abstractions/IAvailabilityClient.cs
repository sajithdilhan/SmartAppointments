using Booking.Application.Models;
using SmartAppointments.BuildingBlocks.Models;

namespace Booking.Application.Abstractions;

/// <summary>
/// Booking's view of Availability's internal API. Expected remote outcomes are values: 404 and 409
/// carry Availability's reason, and every outage (network, timeout, open circuit, 401, 5xx) is
/// <c>Failure(503)</c>. No Polly type crosses this boundary.
/// </summary>
public interface IAvailabilityClient
{
    Task<Result<SlotInfo>> GetSlotAsync(Guid slotId, CancellationToken cancellationToken);

    Task<Result<bool>> ReserveAsync(Guid slotId, Guid appointmentId, CancellationToken cancellationToken);

    Task<Result<bool>> ReleaseAsync(Guid slotId, Guid appointmentId, CancellationToken cancellationToken);
}
