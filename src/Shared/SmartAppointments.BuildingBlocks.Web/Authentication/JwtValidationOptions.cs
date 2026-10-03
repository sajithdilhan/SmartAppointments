namespace SmartAppointments.BuildingBlocks.Web.Authentication;

/// <summary>
/// The part of the <c>Jwt</c> configuration section that every service needs to validate the
/// tokens Auth issues. Auth binds the same section into its own options type, which adds the
/// token lifetimes it signs with.
/// </summary>
public sealed class JwtValidationOptions
{
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
}
