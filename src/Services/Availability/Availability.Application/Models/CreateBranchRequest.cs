namespace Availability.Application.Models;

public sealed record CreateBranchRequest(
    string Code,
    string Name,
    string? Description,
    string Address,
    string PhoneNumber);
