namespace Auth.Application.Models;

// string? on purpose, as in RefreshTokenRequest: a missing value is the validator's 400,
// while an empty one is a successful logout.
public sealed record LogoutRequest(string? RefreshToken);
