namespace Auth.Application.Models;

/// <summary>
/// The only user fields the internal lookup exposes: display data, never an email or a role.
/// </summary>
public sealed record UserLookupItem(Guid Id, string FirstName, string LastName, string PhoneNumber);
