namespace Auth.Application.Models;

public sealed record RegisterCustomerRequest(
    string FirstName,
    string LastName,
    string Email,
    string PhoneNumber,
    string Password);
