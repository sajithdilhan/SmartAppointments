using Booking.Application.Abstractions;
using Microsoft.Extensions.Caching.Memory;
using SmartAppointments.BuildingBlocks.Models;

namespace Booking.Infrastructure.Clients;

/// <summary>
/// Reads a branch's time zone from Availability and keeps it for five minutes. A null zone
/// (unknown branch, or no schedule yet) is cached too; a failure never is.
/// </summary>
public sealed class CachedBranchTimeZoneProvider(IAvailabilityClient availability, IMemoryCache cache) : IBranchTimeZoneProvider
{
    public static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    private sealed record Entry(string? ZoneId);

    public async Task<Result<string?>> GetAsync(Guid branchId, CancellationToken cancellationToken)
    {
        var key = $"branch-zone:{branchId}";
        if (cache.TryGetValue(key, out Entry? cached) && cached is not null)
        {
            return Result<string?>.Success(cached.ZoneId);
        }

        var result = await availability.GetBranchAsync(branchId, cancellationToken);
        if (!result.IsSuccess)
        {
            return Result<string?>.Failure(result.Error!);
        }

        var zoneId = result.Value?.TimeZoneId;
        cache.Set(key, new Entry(zoneId), CacheDuration);
        return Result<string?>.Success(zoneId);
    }
}
