using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Text;

namespace SmartAppointments.BuildingBlocks.Web.Authentication;

public static class AuthenticationExtensions
{
    /// <summary>
    /// The shortest HS256 key that is not trivially brute-forceable. A shorter one is a
    /// configuration mistake, not a preference, so startup fails rather than using it.
    /// </summary>
    public const int MinimumSecretKeyBytes = 32;

    /// <summary>
    /// Validates bearer tokens signed by the Auth service. Every service must be configured with
    /// Auth's <c>Jwt:Issuer</c>, <c>Jwt:Audience</c> and <c>Jwt:SecretKey</c>.
    /// </summary>
    public static IServiceCollection AddJwtAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection("Jwt").Get<JwtValidationOptions>()
            ?? throw new InvalidOperationException("The 'Jwt' configuration section is missing.");

        // The secret is deliberately absent from appsettings.json, so an unconfigured environment
        // must fail loudly at startup rather than fall back to a value committed to source control.
        if (string.IsNullOrWhiteSpace(settings.SecretKey))
        {
            throw new InvalidOperationException(
                "'Jwt:SecretKey' is not configured. Set it to the Auth service's signing key in " +
                "appsettings.Development.json or with 'dotnet user-secrets set \"Jwt:SecretKey\" \"<key>\"' " +
                "for local development, or through the environment in every other environment.");
        }

        if (Encoding.UTF8.GetByteCount(settings.SecretKey) < MinimumSecretKeyBytes)
        {
            throw new InvalidOperationException(
                $"'Jwt:SecretKey' must be at least {MinimumSecretKeyBytes} bytes long for HMAC-SHA256 tokens.");
        }

        if (string.IsNullOrWhiteSpace(settings.Issuer) || string.IsNullOrWhiteSpace(settings.Audience))
        {
            throw new InvalidOperationException("'Jwt:Issuer' and 'Jwt:Audience' must both be configured.");
        }

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.MapInboundClaims = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = settings.Issuer,
                ValidAudience = settings.Audience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.SecretKey)),
                NameClaimType = ClaimTypes.Name,
                RoleClaimType = Constants.RoleClaimType,
                ClockSkew = TimeSpan.Zero
            };
        });
        return services;
    }

    /// <summary>
    /// Registers the named policies. A policy name must mean the same role set in every service.
    /// </summary>
    public static IServiceCollection AddAuthorizationWithRoles(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .AddPolicy(Constants.AdminPolicy, policy => policy.RequireRole(Constants.AdminRole))
            .AddPolicy(Constants.StaffPolicy, policy => policy.RequireRole(Constants.StaffRole))
            .AddPolicy(Constants.AdminOrStaffPolicy, policy => policy.RequireRole(Constants.AdminRole, Constants.StaffRole))
            .AddPolicy(Constants.CustomerPolicy, policy => policy.RequireRole(Constants.CustomerRole))
            .AddPolicy(Constants.AllowedOriginsPolicy, policy => policy.RequireRole(Constants.AdminRole, Constants.StaffRole, Constants.CustomerRole));
        return services;
    }
}
