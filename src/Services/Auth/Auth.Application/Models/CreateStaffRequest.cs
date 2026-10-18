namespace Auth.Application.Models;

public sealed record CreateStaffRequest(
    string FirstName,
    string LastName,
    string Email,
    string PhoneNumber,
    string Password,
    Guid BranchId);
