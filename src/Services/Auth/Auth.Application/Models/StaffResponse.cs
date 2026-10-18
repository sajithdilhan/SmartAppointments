using Auth.Domain.Entities;

namespace Auth.Application.Models;

public sealed record StaffResponse(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    string PhoneNumber,
    string Role,
    Guid BranchId)
{
    public static StaffResponse From(User user) => new(
        user.Id,
        user.FirstName,
        user.LastName,
        user.Email.Value,
        user.PhoneNumber,
        user.Role.ToString(),
        user.BranchId!.Value);
}
