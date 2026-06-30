namespace Auth.Application.Models;

public sealed record GetCustomerResponse(string FirstName, string LastName, string Email, string PhoneNumber, bool IsActive);