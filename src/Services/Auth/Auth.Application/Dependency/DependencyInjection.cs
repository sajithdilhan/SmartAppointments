using Auth.Application.Commands;
using Auth.Application.Models;
using Auth.Application.Validations;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using SmartAppointments.BuildingBlocks;
using System.Security.Claims;
using System.Text;

namespace Auth.Application.Dependency;

public static class DependencyInjection
{
    private const string BearerSecurityScheme = "Bearer";
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Register application services here
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(DependencyInjection).Assembly));
        services.AddScoped<IValidator<RegisterCustomerCommand>, RegisterCustomerCommandValidator>();
        services.AddScoped<IValidator<LoginUserCommand>, LoginUserRequestValidator>();

        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Components ??= new OpenApiComponents();
                document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
                document.Components.SecuritySchemes[BearerSecurityScheme] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                    Description = "JWT bearer token authentication. Enter the token without the 'Bearer' prefix."
                };

                document.Security ??= [];
                document.Security.Add(new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference(BearerSecurityScheme, document)] = []
                });

                return Task.CompletedTask;
            });
        });
        return services;
    }

    /// <summary>
    /// The shortest HS256 key that is not trivially brute-forceable. A shorter one is a
    /// configuration mistake, not a preference, so startup fails rather than signing with it.
    /// </summary>
    private const int MinimumSecretKeyBytes = 32;

    public static IServiceCollection AddAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var authSettings = configuration.GetSection("Jwt").Get<JwtOptions>()
            ?? throw new InvalidOperationException("The 'Jwt' configuration section is missing.");

        // The secret is deliberately absent from appsettings.json, so an unconfigured environment
        // must fail loudly at startup rather than fall back to a value committed to source control.
        if (string.IsNullOrWhiteSpace(authSettings.SecretKey))
        {
            throw new InvalidOperationException(
                "'Jwt:SecretKey' is not configured. Set it with 'dotnet user-secrets set \"Jwt:SecretKey\" \"<key>\"' " +
                "for local development, or through the environment in every other environment.");
        }

        if (Encoding.UTF8.GetByteCount(authSettings.SecretKey) < MinimumSecretKeyBytes)
        {
            throw new InvalidOperationException(
                $"'Jwt:SecretKey' must be at least {MinimumSecretKeyBytes} bytes long to sign HMAC-SHA256 tokens.");
        }

        if (string.IsNullOrWhiteSpace(authSettings.Issuer) || string.IsNullOrWhiteSpace(authSettings.Audience))
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
                ValidIssuer = authSettings.Issuer,
                ValidAudience = authSettings.Audience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(authSettings.SecretKey)),
                NameClaimType = ClaimTypes.Name,
                RoleClaimType = Constants.RoleClaimType,
                ClockSkew = TimeSpan.Zero
            };
        });
        return services;
    }

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
