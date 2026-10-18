namespace Booking.Application.Models;

/// <summary>What Availability returns for a branch; mirrors its <c>InternalBranchResponse</c>.</summary>
public sealed record BranchInfo(Guid Id, bool IsActive, string? TimeZoneId);
