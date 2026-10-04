namespace Auth.Application.Models;

public sealed record AccessToken(string Value, DateTime ExpiresAtUtc);
