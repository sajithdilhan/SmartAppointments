using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;

namespace SmartAppointments.BuildingBlocks.Web.Authentication;

/// <summary>
/// Authenticates service-to-service calls by the <c>X-API-Key</c> header. The principal it builds
/// carries no role claims, so a request that passes here can satisfy no role policy even if the
/// scheme is attached to one by mistake.
/// </summary>
public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<ApiKeyAuthenticationOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<ApiKeyAuthenticationOptions>(options, logger, encoder)
{
    public const string ServiceName = "internal-service";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(Constants.ApiKeyHeaderName, out var presented) || presented.Count == 0)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        // Hashing first gives equal-length inputs, so neither the content nor the length of the
        // configured key leaks through the comparison time.
        var presentedDigest = SHA256.HashData(Encoding.UTF8.GetBytes(presented.ToString()));
        var configuredDigest = SHA256.HashData(Encoding.UTF8.GetBytes(Options.ApiKey));

        if (!CryptographicOperations.FixedTimeEquals(presentedDigest, configuredDigest))
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));
        }

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, ServiceName)],
            Scheme.Name);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
