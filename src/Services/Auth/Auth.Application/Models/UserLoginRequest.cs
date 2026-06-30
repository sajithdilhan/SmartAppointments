namespace Auth.Application.Models;

public sealed record UserLoginRequest(string Email, string Password);
