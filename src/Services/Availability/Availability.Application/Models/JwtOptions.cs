namespace Availability.Application.Models;

/// <summary>
/// The subset of Auth's JWT settings that a token consumer needs. This service validates tokens
/// and never issues them, so the lifetime settings have no meaning here.
/// </summary>
public sealed class JwtOptions
{
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
}
