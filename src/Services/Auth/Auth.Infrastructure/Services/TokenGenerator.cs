using Auth.Application.Abstractions;
using Auth.Application.Models;
using Auth.Domain.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SmartAppointments.BuildingBlocks;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace Auth.Infrastructure.Services;

public class TokenGenerator(IOptions<JwtOptions> options) : ITokenGenerator
{
    public string GenerateAccessToken(User user)
    {
        ArgumentNullException.ThrowIfNull(user, nameof(user));
        ArgumentNullException.ThrowIfNull(options?.Value, nameof(options));

        var expirationMinutes = options.Value.AccessTokenExpirationMinutes;
        if (expirationMinutes <= 0)
        {
            throw new InvalidOperationException(
                $"{nameof(JwtOptions.AccessTokenExpirationMinutes)} must be greater than zero.");
        }

        var key = Encoding.UTF8.GetBytes(options.Value.SecretKey);
        var securityKey = new SymmetricSecurityKey(key);
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            // Short claim names are emitted deliberately: JwtBearer is configured with
            // MapInboundClaims = false, so whatever is written here is what is read back.
            Subject = new ClaimsIdentity(new[]
            {
            new Claim(Constants.UserIdClaimType, user.Id.ToString()),
            new Claim(Constants.EmailClaimType, user.Email.Value),
            new Claim(Constants.RoleClaimType, user.Role.ToString())
        }),
            Expires = DateTime.UtcNow.AddMinutes(expirationMinutes),
            Issuer = options.Value.Issuer,
            Audience = options.Value.Audience,
            SigningCredentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256Signature)
        };
        var tokenHandler = new JwtSecurityTokenHandler();
        return tokenHandler.WriteToken(tokenHandler.CreateToken(tokenDescriptor));
    }

    public string GenerateRefreshToken()
    {
        var randomNumber = RandomNumberGenerator.GetBytes(64); // Non-obsolete modern random byte retrieval
        return Convert.ToBase64String(randomNumber);
    }
}
