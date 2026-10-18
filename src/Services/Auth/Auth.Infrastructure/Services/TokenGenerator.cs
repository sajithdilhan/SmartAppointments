using Auth.Application.Abstractions;
using Auth.Application.Models;
using Auth.Domain.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SmartAppointments.BuildingBlocks;
using SmartAppointments.BuildingBlocks.Enums;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace Auth.Infrastructure.Services;

public class TokenGenerator(IOptions<JwtOptions> options, TimeProvider timeProvider) : ITokenGenerator
{
    public AccessToken GenerateAccessToken(User user)
    {
        ArgumentNullException.ThrowIfNull(user, nameof(user));
        ArgumentNullException.ThrowIfNull(options?.Value, nameof(options));

        var expirationMinutes = options.Value.AccessTokenExpirationMinutes;
        if (expirationMinutes <= 0)
        {
            throw new InvalidOperationException(
                $"{nameof(JwtOptions.AccessTokenExpirationMinutes)} must be greater than zero.");
        }

        // iat and nbf come from the same clock as exp; left unset, the handler would fill them from
        // DateTime.UtcNow and reject an exp that is not after them.
        var now = timeProvider.GetUtcNow().UtcDateTime;

        // A JWT exp claim is whole seconds, so truncate here and return the exact value that is signed.
        var expires = now.AddMinutes(expirationMinutes);
        expires = new DateTime(expires.Ticks - expires.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);

        var key = Encoding.UTF8.GetBytes(options.Value.SecretKey);
        var securityKey = new SymmetricSecurityKey(key);
        var claims = new List<Claim>
        {
            new(Constants.UserIdClaimType, user.Id.ToString()),
            new(Constants.EmailClaimType, user.Email.Value),
            new(Constants.RoleClaimType, user.Role.ToString())
        };

        // Only a Staff user with a branch carries it; read from the user passed in, so a refresh
        // (which re-reads the user) always reflects the current assignment.
        if (user.Role == UserRole.Staff && user.BranchId is { } branchId)
        {
            claims.Add(new Claim(Constants.BranchIdClaimType, branchId.ToString()));
        }

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            // Short claim names are emitted deliberately: JwtBearer is configured with
            // MapInboundClaims = false, so whatever is written here is what is read back.
            Subject = new ClaimsIdentity(claims),
            IssuedAt = now,
            NotBefore = now,
            Expires = expires,
            Issuer = options.Value.Issuer,
            Audience = options.Value.Audience,
            SigningCredentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256Signature)
        };
        var tokenHandler = new JwtSecurityTokenHandler();
        return new AccessToken(tokenHandler.WriteToken(tokenHandler.CreateToken(tokenDescriptor)), expires);
    }

    public string GenerateRefreshToken()
    {
        var randomNumber = RandomNumberGenerator.GetBytes(64); // Non-obsolete modern random byte retrieval
        return Convert.ToBase64String(randomNumber);
    }
}
