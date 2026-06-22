namespace Auth.Application.Models;

public sealed record RegisterCustomerResponse(
    Guid UserId,
    string Email,
    string Role);
