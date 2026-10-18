namespace Availability.Application.Models;

/// <summary>
/// What other services need to know about a branch: that it exists, whether it is open and its
/// time zone (null until a schedule has been set).
/// </summary>
public sealed record InternalBranchResponse(Guid Id, bool IsActive, string? TimeZoneId);
