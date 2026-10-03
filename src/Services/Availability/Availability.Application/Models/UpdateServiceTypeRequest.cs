namespace Availability.Application.Models;

/// <summary>
/// Has no <c>Code</c>: the code is immutable, and a <c>code</c> field in the JSON body is simply
/// not bound.
/// </summary>
public sealed record UpdateServiceTypeRequest(
    string Name,
    string? Description,
    int DurationMinutes);
