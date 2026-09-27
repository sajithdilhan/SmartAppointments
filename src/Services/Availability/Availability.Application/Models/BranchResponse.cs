using Availability.Domain.Entities;

namespace Availability.Application.Models;

public sealed record BranchResponse(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    string Address,
    string PhoneNumber,
    bool IsActive,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc)
{
    public static BranchResponse From(Branch branch) => new(
        branch.Id,
        branch.Code,
        branch.Name,
        branch.Description,
        branch.Address,
        branch.PhoneNumber,
        branch.IsActive,
        branch.CreatedAtUtc,
        branch.UpdatedAtUtc);
}
