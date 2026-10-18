using Auth.Application.Abstractions;
using Microsoft.Extensions.Logging;
using SmartAppointments.BuildingBlocks.Models;

namespace Auth.Application.Handlers;

/// <summary>
/// The branch rule shared by creating a staff member and moving one: the branch must exist and be
/// active in Availability.
/// </summary>
internal static class StaffBranchCheck
{
    /// <summary>Null when the branch is usable; otherwise the 422 or 503 error.</summary>
    public static async Task<Error?> CheckAsync(
        IAvailabilityClient availabilityClient,
        Guid branchId,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var branch = await availabilityClient.GetBranchAsync(branchId, cancellationToken);
        if (!branch.IsSuccess)
        {
            logger.LogWarning("Availability could not confirm branch {BranchId}", branchId);
            return branch.Error!;
        }

        if (branch.Value is null || !branch.Value.IsActive)
        {
            logger.LogWarning("Branch {BranchId} does not exist or is inactive", branchId);
            return new Error(422, $"Branch '{branchId}' does not exist or is inactive.");
        }

        return null;
    }
}
