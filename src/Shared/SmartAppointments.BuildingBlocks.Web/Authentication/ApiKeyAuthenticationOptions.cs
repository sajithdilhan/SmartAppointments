using Microsoft.AspNetCore.Authentication;

namespace SmartAppointments.BuildingBlocks.Web.Authentication;

/// <summary>
/// Options of the API-key scheme. <see cref="ApiKey"/> is the key every caller of the internal
/// endpoints must present; it is bound from <c>InternalApi:Key</c> by
/// <see cref="ApiKeyAuthenticationExtensions.AddApiKeyAuthentication"/>.
/// </summary>
public sealed class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
    public string ApiKey { get; set; } = string.Empty;
}
