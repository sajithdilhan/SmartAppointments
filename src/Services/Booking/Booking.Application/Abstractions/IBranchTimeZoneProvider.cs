using SmartAppointments.BuildingBlocks.Models;

namespace Booking.Application.Abstractions;

public interface IBranchTimeZoneProvider
{
    /// <summary>
    /// Success(zoneId or null): null means "no schedule or unknown branch", use UTC.
    /// Failure(503) when Availability is down and nothing is cached.
    /// </summary>
    Task<Result<string?>> GetAsync(Guid branchId, CancellationToken cancellationToken);
}
