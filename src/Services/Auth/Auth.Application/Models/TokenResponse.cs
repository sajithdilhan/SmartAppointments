namespace Auth.Application.Models;

public sealed record TokenResponse(string AccessToken, string RefreshToken);

