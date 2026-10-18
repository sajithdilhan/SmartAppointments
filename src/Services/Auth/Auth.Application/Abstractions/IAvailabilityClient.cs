using SmartAppointments.BuildingBlocks.Models;

namespace Auth.Application.Abstractions;

/// <summary>
/// Reads branch data from the Availability service's internal API.
/// </summary>
public interface IAvailabilityClient
{
    /// <summary>
    /// Success(null) means Availability answered 404; Failure(503) means unreachable, a timeout, a
    /// 401/403 from Availability (a wrong API key) or a 5xx after the retries.
    /// </summary>
    Task<Result<BranchInfo?>> GetBranchAsync(Guid branchId, CancellationToken cancellationToken);
}

public sealed record BranchInfo(Guid Id, bool IsActive, string? TimeZoneId);
